using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Collections.Concurrent;
using Confluent.Kafka;
using Shared.Models;
using Data.Mongo;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace SensorDataSaver
{
    public class SensorDataSaver : BackgroundService, ISensorDataSaver
    {
        private readonly IConsumer<string, string> _kafkaConsumer;
        private readonly ConcurrentDictionary<int, PanelReading> _lastPanelReadings 
            = new();
        private readonly ILogger<SensorDataSaver> _logger;
        private readonly IServiceProvider _provider;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ConcurrentDictionary<string, Panel> _panels = new();
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _sensorInfoServerBaseUrl;

        public SensorDataSaver(
            IOptions<KafkaOptions> kafkaOptions,
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

            var kafka = kafkaOptions.Value;

            // Configure JSON serializer options
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Configure Kafka consumer
            var config = new ConsumerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                GroupId = kafka.ClientId ?? "sensor-data-saver",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = true,
                AutoCommitIntervalMs = 5000,
                // Performance settings
                FetchMinBytes = 1,
                FetchWaitMaxMs = 100
            };

            // Add authentication if configured
            if (!string.IsNullOrEmpty(kafka.SaslUsername))
            {
                config.SecurityProtocol = SecurityProtocol.SaslSsl;
                config.SaslMechanism = SaslMechanism.Plain;
                config.SaslUsername = kafka.SaslUsername;
                config.SaslPassword = kafka.SaslPassword;
            }

            _kafkaConsumer = new ConsumerBuilder<string, string>(config)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Reason}", e.Reason))
                .SetLogHandler((_, m) => _logger.LogInformation("Kafka log: {Message}", m.Message))
                .Build();

            // Fetch panels from SensorInfoServer
            _ = Task.Run(async () => await LoadPanelsAsync());
        }

        private async Task LoadPanelsAsync()
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
            // Subscribe to Kafka topic
            _kafkaConsumer.Subscribe("sensor-data");
            _logger.LogInformation("Subscribed to Kafka topic: {Topic}", "sensor-data");

            // Start Kafka consumer task
            var consumerTask = Task.Run(() => ConsumeKafkaMessagesAsync(stoppingToken), stoppingToken);

            // 1. Wait until the next round minute
            await WaitUntilNextFullMinute(stoppingToken);

            // 2. Now run every 1 minute aligned with the clock
            var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

            do 
            {
                using var scope = _provider.CreateScope();
                var now = DateTime.Now;
                var panelReadingRepo = scope.ServiceProvider.GetRequiredService<IPanelReadingRepository>();
                var panelReadings = _lastPanelReadings.Values.Where(x => now - x.ReadingTime < TimeSpan.FromSeconds(90)).ToList();

                if (panelReadings.Count == 0)
                {
                    continue;
                }
                
                if(panelReadings.Count > 0)
                {
                    await panelReadingRepo.AddAsync(panelReadings);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));

            // Wait for consumer to finish
            await consumerTask;
        }

        private async Task ConsumeKafkaMessagesAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = _kafkaConsumer.Consume(TimeSpan.FromMilliseconds(1000));
                        
                        if (result == null)
                        {
                            continue;
                        }

                        if (result.IsPartitionEOF)
                        {
                            _logger.LogDebug("Reached end of partition {Partition}", result.Partition);
                            continue;
                        }

                        // Deserialize message
                        try
                        {
                            var message = JsonSerializer.Deserialize<SensorDataMessageModel>(
                                result.Message.Value, 
                                _jsonOptions);

                            if (message != null)
                            {
                                await ProcessSensorDataMessage(message);
                            }
                            else
                            {
                                _logger.LogWarning("Failed to deserialize message from Kafka. Topic: {Topic}, Partition: {Partition}, Offset: {Offset}",
                                    result.Topic, result.Partition, result.Offset);
                            }
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "Error deserializing Kafka message. Topic: {Topic}, Partition: {Partition}, Offset: {Offset}",
                                result.Topic, result.Partition, result.Offset);
                        }
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Error consuming from Kafka: {Reason}", ex.Error.Reason);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in Kafka consumer");
            }
        }

        private async Task ProcessSensorDataMessage(SensorDataMessageModel message)
        {
            if(message.GatewayData == null)
            {
                _logger.LogError("Invalid gateway data: {GatewayId}", message.GatewayId);
                return;
            }

            using var scope = _provider.CreateScope();

            for(var i = 0; i < message.GatewayData.Sensors.Count; i++)
            {
                var sensor = message.GatewayData.Sensors[i];
                var sensorKey = $"{message.GatewayId}-{i}";
                if (!_panels.TryGetValue(sensorKey, out var panel))
                {
                    _logger.LogError("Panel not found: {Index} for GatewayId: {GatewayId}", i, message.GatewayId);
                    continue;
                }
                var panelReading = new PanelReading
                {
                    PanelId = panel.Id,
                    ReadingTime = message.GatewayData.Timestamp,
                    Value = sensor.Value,
                };

                _lastPanelReadings.AddOrUpdate(panel.Id, panelReading, (key, existing) => panelReading);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Kafka consumer...");
            _kafkaConsumer?.Close();
            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _kafkaConsumer?.Dispose();
            base.Dispose();
        }
    }
}
