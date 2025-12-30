using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Collections.Concurrent;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Models;
using Data.Mongo;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace SensorDataSaver
{
    public class SensorDataSaver : BackgroundService, ISensorDataSaver
    {
        private readonly IConnection _rabbitMqConnection;
        private readonly IModel _rabbitMqChannel;
        private readonly string _queueName;
        private readonly ConcurrentDictionary<int, PanelReading> _lastPanelReadings
            = new();
        private readonly ILogger<SensorDataSaver> _logger;
        private readonly IServiceProvider _provider;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ConcurrentDictionary<string, Panel> _panels = new();
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _sensorInfoServerBaseUrl;

        public SensorDataSaver(
            IOptions<RabbitMQOptions> rabbitMqOptions,
            ILogger<SensorDataSaver> logger,
            IServiceProvider provider,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _provider = provider;
            _httpClientFactory = httpClientFactory;
            _sensorInfoServerBaseUrl = configuration.GetSection("SensorInfoServer")["BaseUrl"]
                ?? "http://localhost:5000";

            var rabbitMq = rabbitMqOptions.Value;

            // Configure JSON serializer options
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Configure RabbitMQ connection
            var factory = new ConnectionFactory
            {
                HostName = rabbitMq.HostName,
                Port = rabbitMq.Port,
                VirtualHost = rabbitMq.VirtualHost ?? "/",
                UserName = rabbitMq.UserName ?? "guest",
                Password = rabbitMq.Password ?? "guest",
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _logger.LogInformation(
                "Attempting to connect to RabbitMQ at {HostName}:{Port} with user {UserName} (VirtualHost: {VirtualHost})",
                rabbitMq.HostName,
                rabbitMq.Port,
                rabbitMq.UserName ?? "guest",
                rabbitMq.VirtualHost ?? "/");

            try
            {
                _rabbitMqConnection = factory.CreateConnection();
                _rabbitMqChannel = _rabbitMqConnection.CreateModel();
                _logger.LogInformation("Successfully connected to RabbitMQ");
            }
            catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException ex)
            {
                _logger.LogError(ex,
                    "Failed to connect to RabbitMQ broker at {HostName}:{Port}. " +
                    "Please ensure RabbitMQ is running and accessible. " +
                    "Inner exception: {InnerException}",
                    rabbitMq.HostName,
                    rabbitMq.Port,
                    ex.InnerException?.Message ?? "None");
                throw;
            }
            catch (RabbitMQ.Client.Exceptions.AuthenticationFailureException ex)
            {
                _logger.LogError(ex,
                    "RabbitMQ authentication failed for user '{UserName}'. " +
                    "Please verify the username and password in appsettings.json",
                    rabbitMq.UserName ?? "guest");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error connecting to RabbitMQ at {HostName}:{Port}",
                    rabbitMq.HostName,
                    rabbitMq.Port);
                throw;
            }

            _queueName = rabbitMq.QueueName ?? "sensor-data-saver";

            // Declare exchange
            _rabbitMqChannel.ExchangeDeclare(
                exchange: "sensor-data",
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);

            // Declare queue
            _rabbitMqChannel.QueueDeclare(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false);

            // Bind queue to exchange with routing key pattern
            _rabbitMqChannel.QueueBind(
                queue: _queueName,
                exchange: "sensor-data",
                routingKey: "#");

            _logger.LogInformation("RabbitMQ consumer configured. Exchange: {Exchange}, Queue: {Queue}, RoutingKey: {RoutingKey}",
                "sensor-data", _queueName, "#");

            // Fetch panels from SensorInfoServer
            _ = Task.Run(async () => await LoadPanelsAsync());
        }

        private async Task LoadPanelsAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    var url = $"{_sensorInfoServerBaseUrl}/api/sensorInfo";
                    _logger.LogInformation("Fetching panels from SensorInfoServer: {Url}", url);

                    var response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var jsonContent = await response.Content.ReadAsStringAsync();
                    var panels = JsonSerializer.Deserialize<IEnumerable<Panel>>(jsonContent, _jsonOptions);

                    if (panels != null)
                    {
                        foreach (var panel in panels)
                        {
                            var panelKey = $"{panel.GatewayId}-{panel.Index}";
                            _panels.AddOrUpdate(panelKey, panel, (key, existing) => panel);
                            _logger.LogDebug("Loaded panel: {Key} (Id: {Id}, Name: {Name})", panelKey, panel.Id, panel.Name);
                        }
                        _logger.LogInformation("Successfully loaded {Count} panels from SensorInfoServer", _panels.Count);
                        return;
                    }
                    else
                    {
                        _logger.LogWarning("No panels returned from SensorInfoServer");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading panels from SensorInfoServer");
                }
            }
            _logger.LogError("Failed to load panels from SensorInfoServer after 10 attempts");
        }

        public Task<bool> SubscribeAsync(string topic)
        {
            return Task.FromResult(true);
        }

        private static async Task WaitUntilNextFullMinute(CancellationToken stoppingToken)
        {
            var now = DateTime.Now;
            var nextMinute = now.AddMinutes(1);
            var aligned = new DateTime(
                nextMinute.Year, nextMinute.Month, nextMinute.Day,
                nextMinute.Hour, nextMinute.Minute, 0);

            var delay = aligned - now;

            await Task.Delay(delay, stoppingToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Subscribed to RabbitMQ exchange: {Exchange}, queue: {Queue}", "sensor-data", _queueName);

            var consumerTask = Task.Run(() => ConsumeRabbitMQMessagesAsync(stoppingToken), stoppingToken);

            try
            {
                await WaitUntilNextFullMinute(stoppingToken);
                await RunPeriodicPersistenceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Periodic persistence cancelled");
            }
            finally
            {
                await consumerTask;
            }
        }

        private async Task RunPeriodicPersistenceAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

            do
            {
                await PersistRecentPanelReadingsAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task PersistRecentPanelReadingsAsync(CancellationToken stoppingToken)
        {
            var cutoffTime = DateTime.Now.AddSeconds(-90);
            var recentReadings = _lastPanelReadings.Values
                .Where(x => x.ReadingTime >= cutoffTime)
                .ToList();

            if (recentReadings.Count == 0)
            {
                return;
            }

            using var scope = _provider.CreateScope();
            var panelReadingRepo = scope.ServiceProvider.GetRequiredService<IPanelReadingRepository>();

            try
            {
                await panelReadingRepo.AddAsync(recentReadings);
                _logger.LogDebug("Persisted {Count} panel readings", recentReadings.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist {Count} panel readings", recentReadings.Count);
            }
        }

        private async Task ConsumeRabbitMQMessagesAsync(CancellationToken stoppingToken)
        {
            try
            {
                var consumer = new EventingBasicConsumer(_rabbitMqChannel);
                consumer.Received += async (model, ea) => await HandleMessageAsync(ea, stoppingToken);

                _rabbitMqChannel.BasicConsume(
                    queue: _queueName,
                    autoAck: false,
                    consumer: consumer);

                _logger.LogInformation("RabbitMQ consumer started");

                await WaitForCancellationAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in RabbitMQ consumer");
                throw;
            }
        }

        private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken stoppingToken)
        {
            try
            {
                var body = ea.Body.ToArray();
                var messageText = Encoding.UTF8.GetString(body);

                var message = DeserializeMessage(messageText, ea.RoutingKey);

                if (message != null)
                {
                    await ProcessSensorDataMessage(message);
                    _rabbitMqChannel.BasicAck(ea.DeliveryTag, false);
                }
                else
                {
                    _rabbitMqChannel.BasicNack(ea.DeliveryTag, false, false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing RabbitMQ message");
                _rabbitMqChannel.BasicNack(ea.DeliveryTag, false, true);
            }
        }

        private SensorDataMessageModel? DeserializeMessage(string messageText, string routingKey)
        {
            try
            {
                var message = JsonSerializer.Deserialize<SensorDataMessageModel>(messageText, _jsonOptions);

                if (message == null)
                {
                    _logger.LogWarning("Failed to deserialize message from RabbitMQ. RoutingKey: {RoutingKey}", routingKey);
                }

                return message;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error deserializing RabbitMQ message. RoutingKey: {RoutingKey}", routingKey);
                return null;
            }
        }

        private async Task ProcessSensorDataMessage(SensorDataMessageModel message)
        {
            if (message.GatewayData?.Sensors == null)
            {
                _logger.LogError("Invalid gateway data: {GatewayId}", message.GatewayId);
                return;
            }

            var processedCount = 0;
            var timestamp = message.GatewayData.Timestamp;

            for (var i = 0; i < message.GatewayData.Sensors.Count; i++)
            {
                if (ProcessSensorReading(message.GatewayId, i, message.GatewayData.Sensors[i], timestamp))
                {
                    processedCount++;
                }
            }

            if (processedCount > 0)
            {
                _logger.LogDebug("Processed {Count}/{Total} sensor readings for gateway {GatewayId}",
                    processedCount, message.GatewayData.Sensors.Count, message.GatewayId);
            }
        }

        private bool ProcessSensorReading(string gatewayId, int sensorIndex, SensorData sensor, DateTime timestamp)
        {
            var sensorKey = $"{gatewayId}-{sensorIndex}";

            if (!_panels.TryGetValue(sensorKey, out var panel))
            {
                _logger.LogWarning("Panel not found: {Index} for GatewayId: {GatewayId}", sensorIndex, gatewayId);
                return false;
            }

            var panelReading = new PanelReading
            {
                PanelId = panel.Id,
                ReadingTime = timestamp,
                Value = sensor.Value,
            };

            _lastPanelReadings.AddOrUpdate(panel.Id, panelReading, (key, existing) => panelReading);
            return true;
        }

        private static async Task WaitForCancellationAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping RabbitMQ consumer...");
            _rabbitMqChannel?.Close();
            _rabbitMqConnection?.Close();
            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _rabbitMqChannel?.Dispose();
            _rabbitMqConnection?.Dispose();
            base.Dispose();
        }
    }
}
