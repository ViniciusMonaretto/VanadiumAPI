using MQTTnet;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using RabbitMQ.Client;
using System.Text.Json;
using System.Text;
using Shared.Models;

namespace Mqtt
{
    public class MqttService : BackgroundService, IMqttService
    {
        private readonly IMqttClient _mqttClient;
        private readonly MqttClientOptions _mqttClientOptions;
        private readonly ILogger<MqttService> _logger;
        private readonly HashSet<string> _subscribedTopics = new();
        private readonly string _brokerHost;
        private readonly int _brokerPort;

        // -------------- RABBITMQ CONNECTION --------------
        private readonly IConnection _rabbitMqConnection;
        private readonly IModel _rabbitMqChannel;
        private readonly string _exchange;

        // -------------- WORKER QUEUE --------------
        private readonly Channel<SensorDataMessageModel> _queue =
            Channel.CreateUnbounded<SensorDataMessageModel>();

        public MqttService(
            IOptions<MqttOptions> mqttOptions, 
            IOptions<RabbitMQOptions> rabbitMqOptions,
            ILogger<MqttService> logger)
        {
            _logger = logger;
            
            // MQTT Setup
            var mqttFactory = new MqttClientFactory();
            _mqttClient = mqttFactory.CreateMqttClient();

            var mqtt = mqttOptions.Value;
            _brokerHost = mqtt.BrokerHost;
            _brokerPort = mqtt.BrokerPort;

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(mqtt.BrokerHost, mqtt.BrokerPort)
                .WithClientId(mqtt.ClientId)
                .WithCleanSession();

            if (!string.IsNullOrEmpty(mqtt.Username))
            {
                optionsBuilder.WithCredentials(mqtt.Username, mqtt.Password);
            }

            _mqttClientOptions = optionsBuilder.Build();

            _mqttClient.ApplicationMessageReceivedAsync += HandleMessageReceived;
            _mqttClient.ConnectedAsync += HandleConnected;
            _mqttClient.DisconnectedAsync += HandleDisconnected;

            // RABBITMQ Setup
            var rabbitMq = rabbitMqOptions.Value;
            _exchange = "sensor-data";

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
                "Attempting to connect to RabbitMQ at {HostName}:{Port} with user {UserName}",
                rabbitMq.HostName,
                rabbitMq.Port,
                rabbitMq.UserName ?? "guest");

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
                    "Please ensure RabbitMQ is running and accessible.",
                    rabbitMq.HostName,
                    rabbitMq.Port);
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
            
            // Declare exchange (topic exchange for routing flexibility)
            _rabbitMqChannel.ExchangeDeclare(
                exchange: _exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);
        }

        // -----------------------------------------
        // MAIN SERVICE LOOP
        // -----------------------------------------
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await ConnectAsync(stoppingToken);

            // Start worker (consumer)
            _ = Task.Run(() => ReportWorkerAsync(stoppingToken), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_mqttClient.IsConnected)
                {
                    _logger.LogWarning("MQTT client disconnected. Attempting to reconnect...");
                    await ConnectAsync(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // -----------------------------------------
        // WORKER (CONSUMER) - PROCESS IN BATCH
        // -----------------------------------------
        private async Task ReportWorkerAsync(CancellationToken token)
        {
            var buffer = new List<SensorDataMessageModel>(capacity: 200);
            var lastProcessTime = DateTime.UtcNow;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Try to read with a short timeout
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    cts.CancelAfter(TimeSpan.FromSeconds(2));

                    try
                    {
                        var item = await _queue.Reader.ReadAsync(cts.Token);
                        buffer.Add(item);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        // Timeout occurred, check if we should process the batch
                    }

                    // Process every 200 messages OR every 2 seconds
                    var timeSinceLastProcess = DateTime.UtcNow - lastProcessTime;
                    if (buffer.Count >= 200 || (buffer.Count > 0 && timeSinceLastProcess.TotalSeconds >= 2))
                    {
                        await ProcessBatchAsync(buffer);
                        buffer.Clear();
                        lastProcessTime = DateTime.UtcNow;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker error");
                }
            }

            // Flush remaining items
            if (buffer.Count > 0)
                await ProcessBatchAsync(buffer);
        }

        // -----------------------------------------
        // PROCESS A BATCH → SEND TO RABBITMQ
        // -----------------------------------------
        private async Task ProcessBatchAsync(List<SensorDataMessageModel> batch)
        {
            try
            {
                _logger.LogInformation("Processing MQTT batch with {Count} items", batch.Count);

                var tasks = new List<Task>();

                foreach (var msg in batch)
                {
                    try
                    {
                        var payload = JsonSerializer.Serialize(msg);
                        var body = Encoding.UTF8.GetBytes(payload);
                        
                        // Use GatewayId as routing key - each gateway gets its own routing key
                        var routingKey = msg.GatewayId;

                        var properties = _rabbitMqChannel.CreateBasicProperties();
                        properties.Persistent = true; // Make messages persistent
                        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        properties.MessageId = Guid.NewGuid().ToString();
                        properties.Headers = new Dictionary<string, object>
                        {
                            { "GatewayId", msg.GatewayId },
                            { "Topic", msg.Topic }
                        };

                        // Publish message asynchronously
                        var task = Task.Run(() =>
                        {
                            _rabbitMqChannel.BasicPublish(
                                exchange: _exchange,
                                routingKey: routingKey,
                                basicProperties: properties,
                                body: body);
                        });
                        tasks.Add(task);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error serializing message for RabbitMQ");
                    }
                }

                // Wait for all publishes to complete
                await Task.WhenAll(tasks);
                
                _logger.LogInformation(
                    "Sent {Count} messages to RabbitMQ exchange {Exchange}", 
                    batch.Count, 
                    _exchange);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MQTT batch to RabbitMQ");
            }
        }

        // -----------------------------------------
        // MQTT HANDLER → PRODUCER (VERY FAST)
        // -----------------------------------------
        private Task HandleMessageReceived(MqttApplicationMessageReceivedEventArgs args)
        {
            try
            {
                var topic = args.ApplicationMessage.Topic;
                var payload = args.ApplicationMessage.ConvertPayloadToString();

                var parts = topic.Split('/');
                if (parts.Length < 5)
                {
                    _logger.LogWarning("Invalid topic format: {Topic}", topic);
                    return Task.CompletedTask;
                }

                var gatewayId = parts[2];
                var command = parts[4];

                switch (command)
                {
                    case "report":
                        var gatewayData = GatewayDataTranslator.Translate(payload);
                        if (gatewayData == null)
                        {
                            _logger.LogError("Invalid gateway data: {Payload}", payload);
                            return Task.CompletedTask;
                        }

                        // Add to queue (non-blocking)
                        _queue.Writer.TryWrite(new SensorDataMessageModel
                        {
                            Topic = topic,
                            GatewayId = gatewayId,
                            GatewayData = gatewayData
                        });
                        break;
                    default:
                        _logger.LogWarning("Unknown command: {Command}", command);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling MQTT message");
            }

            return Task.CompletedTask;
        }

        // -----------------------------------------
        // CONNECT / DISCONNECT / SUBSCRIBE
        // -----------------------------------------
        private async Task ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    var result = await _mqttClient.ConnectAsync(_mqttClientOptions, cancellationToken);

                    if (result.ResultCode == MqttClientConnectResultCode.Success)
                    {
                        foreach (var topic in _subscribedTopics)
                            await SubscribeAsync(topic);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to MQTT broker");
            }
        }

        private async Task HandleConnected(MqttClientConnectedEventArgs args)
        {
            await SubscribeAsync("iocloud/response/#");
        }

        private Task HandleDisconnected(MqttClientDisconnectedEventArgs args)
        {
            _logger.LogWarning("MQTT client disconnected: {Reason}", args.Reason);
            return Task.CompletedTask;
        }

        public async Task SubscribeAsync(string topic)
        {
            if (!_mqttClient.IsConnected) return;

            var options = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(topic)
                .Build();

            var result = await _mqttClient.SubscribeAsync(options);

            if (result.Items.Any(x => x.ResultCode == MqttClientSubscribeResultCode.GrantedQoS0 ||
                                      x.ResultCode == MqttClientSubscribeResultCode.GrantedQoS1 ||
                                      x.ResultCode == MqttClientSubscribeResultCode.GrantedQoS2))
            {
                _subscribedTopics.Add(topic);
            }
        }

        public async Task UnsubscribeAsync(string topic)
        {
            if (!_mqttClient.IsConnected) return;

            var options = new MqttClientUnsubscribeOptionsBuilder()
                .WithTopicFilter(topic)
                .Build();

            await _mqttClient.UnsubscribeAsync(options);
            _subscribedTopics.Remove(topic);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _queue.Writer.Complete();

            // Close RabbitMQ connection
            _rabbitMqChannel?.Close();
            _rabbitMqConnection?.Close();

            if (_mqttClient.IsConnected)
                await _mqttClient.DisconnectAsync();

            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _rabbitMqChannel?.Dispose();
            _rabbitMqConnection?.Dispose();
            _mqttClient?.Dispose();
            base.Dispose();
        }

        public Task<bool> IsConnectedAsync()
        {
            return Task.FromResult(_mqttClient.IsConnected);
        }

        public async Task<bool> PublishAsync(string topic, string payload, bool retain = false)
        {
            return (await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(retain)
                .Build())).IsSuccess;
        }
    }
}