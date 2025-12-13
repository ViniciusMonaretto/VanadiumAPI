using MQTTnet;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Confluent.Kafka;
using System.Text.Json;
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

        // -------------- KAFKA PRODUCER --------------
        private readonly IProducer<string, string> _kafkaProducer;
        private readonly string _kafkaTopic;

        // -------------- WORKER QUEUE --------------
        private readonly Channel<SensorDataMessageModel> _queue =
            Channel.CreateUnbounded<SensorDataMessageModel>();

        public MqttService(
            IOptions<MqttOptions> mqttOptions, 
            IOptions<KafkaOptions> kafkaOptions,
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

            // KAFKA Setup
            var kafka = kafkaOptions.Value;
            _kafkaTopic = kafka.Topic;

            var config = new ProducerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                ClientId = kafka.ClientId ?? "mqtt-kafka-bridge",
                // Configurações de performance
                Acks = Acks.Leader, // Ou Acks.All para maior confiabilidade
                LingerMs = 10, // Aguarda 10ms para batching
                BatchSize = 32768, // 32KB batch
                CompressionType = CompressionType.Snappy,
                // Retry
                MessageSendMaxRetries = 3,
                RetryBackoffMs = 100
            };

            // Adicionar autenticação se necessário
            if (!string.IsNullOrEmpty(kafka.SaslUsername))
            {
                config.SecurityProtocol = SecurityProtocol.SaslSsl;
                config.SaslMechanism = SaslMechanism.Plain;
                config.SaslUsername = kafka.SaslUsername;
                config.SaslPassword = kafka.SaslPassword;
            }

            _kafkaProducer = new ProducerBuilder<string, string>(config)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Reason}", e.Reason))
                .Build();
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
        // PROCESS A BATCH → SEND TO KAFKA
        // -----------------------------------------
        private async Task ProcessBatchAsync(List<SensorDataMessageModel> batch)
        {
            try
            {
                _logger.LogInformation("Processing MQTT batch with {Count} items", batch.Count);

                var tasks = new List<Task<DeliveryResult<string, string>>>();

                foreach (var msg in batch)
                {
                    try
                    {
                        var payload = JsonSerializer.Serialize(msg);
                        
                        var message = new Message<string, string>
                        {
                            Key = msg.GatewayId, // Usa GatewayId como key para particionamento
                            Value = payload,
                            Timestamp = new Timestamp(DateTime.UtcNow)
                        };

                        // Envio assíncrono (mais rápido)
                        var task = _kafkaProducer.ProduceAsync(_kafkaTopic, message);
                        tasks.Add(task);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error serializing message for Kafka");
                    }
                }

                // Aguarda todos os envios
                var results = await Task.WhenAll(tasks);
                
                var successCount = results.Count(r => r.Status == PersistenceStatus.Persisted);
                _logger.LogInformation(
                    "Sent {Success}/{Total} messages to Kafka topic {Topic}", 
                    successCount, 
                    batch.Count, 
                    _kafkaTopic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MQTT batch to Kafka");
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

            // Flush Kafka producer
            _kafkaProducer?.Flush(TimeSpan.FromSeconds(10));

            if (_mqttClient.IsConnected)
                await _mqttClient.DisconnectAsync();

            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _kafkaProducer?.Dispose();
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