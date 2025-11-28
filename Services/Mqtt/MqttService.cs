using MQTTnet;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Data.Sqlite;

namespace Services
{
    public class MqttService : BackgroundService, IMqttService
    {
        private readonly IMqttClient _mqttClient;
        private readonly MqttClientOptions _mqttClientOptions;
        private readonly ILogger<MqttService> _logger;
        private readonly HashSet<string> _subscribedTopics = new();
        private readonly string _brokerHost;
        private readonly int _brokerPort;

        public event EventHandler<MqttMessageReceivedEventArgs>? MessageReceived;

        public MqttService(IOptions<MqttOptions> options, ILogger<MqttService> logger)
        {
            _logger = logger;
            var mqttFactory = new MqttClientFactory();
            _mqttClient = mqttFactory.CreateMqttClient();

            var mqttOptions = options.Value;
            _brokerHost = mqttOptions.BrokerHost;
            _brokerPort = mqttOptions.BrokerPort;
            
            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(mqttOptions.BrokerHost, mqttOptions.BrokerPort)
                .WithClientId(mqttOptions.ClientId)
                .WithCleanSession();
            
            if (!string.IsNullOrEmpty(mqttOptions.Username))
            {
                optionsBuilder.WithCredentials(mqttOptions.Username, mqttOptions.Password);
            }
            
            _mqttClientOptions = optionsBuilder.Build();

            _mqttClient.ApplicationMessageReceivedAsync += HandleMessageReceived;
            _mqttClient.ConnectedAsync += HandleConnected;
            _mqttClient.DisconnectedAsync += HandleDisconnected;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await ConnectAsync(stoppingToken);

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

        private async Task ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    _logger.LogInformation("Connecting to MQTT broker at {BrokerHost}:{BrokerPort}...", 
                        _brokerHost, _brokerPort);
                    
                    var result = await _mqttClient.ConnectAsync(_mqttClientOptions, cancellationToken);
                    
                    if (result.ResultCode == MqttClientConnectResultCode.Success)
                    {
                        _logger.LogInformation("Successfully connected to MQTT broker");
                        
                        // Resubscribe to all previously subscribed topics
                        foreach (var topic in _subscribedTopics)
                        {
                            await SubscribeAsync(topic);
                        }
                    }
                    else
                    {
                        _logger.LogError("Failed to connect to MQTT broker. Result: {ResultCode}", result.ResultCode);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to MQTT broker");
            }
        }

        private Task HandleConnected(MqttClientConnectedEventArgs args)
        {
            _logger.LogInformation("MQTT client connected");
            return Task.CompletedTask;
        }

        private Task HandleDisconnected(MqttClientDisconnectedEventArgs args)
        {
            _logger.LogWarning("MQTT client disconnected. Reason: {Reason}", args.Reason);
            return Task.CompletedTask;
        }

        private Task HandleMessageReceived(MqttApplicationMessageReceivedEventArgs args)
        {
            try
            {
                var topic = args.ApplicationMessage.Topic;
                var payload = args.ApplicationMessage.ConvertPayloadToString();
                var retain = args.ApplicationMessage.Retain;

                _logger.LogDebug("Received MQTT message on topic {Topic}: {Payload}", topic, payload);

                MessageReceived?.Invoke(this, new MqttMessageReceivedEventArgs
                {
                    Topic = topic,
                    Payload = payload,
                    Retain = retain
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling MQTT message");
            }

            return Task.CompletedTask;
        }

        public async Task<bool> IsConnectedAsync()
        {
            return _mqttClient.IsConnected;
        }

        public async Task<bool> PublishAsync(string topic, string payload, bool retain = false)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    _logger.LogWarning("Cannot publish message. MQTT client is not connected.");
                    return false;
                }

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithRetainFlag(retain)
                    .Build();

                var result = await _mqttClient.PublishAsync(message);
                
                if (result.IsSuccess)
                {
                    _logger.LogDebug("Published message to topic {Topic}", topic);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to publish message to topic {Topic}. Reason: {Reason}", 
                        topic, result.ReasonCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing MQTT message to topic {Topic}", topic);
                return false;
            }
        }

        public async Task SubscribeAsync(string topic)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    _logger.LogWarning("Cannot subscribe. MQTT client is not connected.");
                    return;
                }

                var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(topic)
                    .Build();

                var result = await _mqttClient.SubscribeAsync(subscribeOptions);
                
                if (result.Items.Any(x => x.ResultCode == MqttClientSubscribeResultCode.GrantedQoS0 ||
                                         x.ResultCode == MqttClientSubscribeResultCode.GrantedQoS1 ||
                                         x.ResultCode == MqttClientSubscribeResultCode.GrantedQoS2))
                {
                    _subscribedTopics.Add(topic);
                    _logger.LogInformation("Subscribed to topic {Topic}", topic);
                }
                else
                {
                    _logger.LogWarning("Failed to subscribe to topic {Topic}", topic);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to topic {Topic}", topic);
            }
        }

        public async Task UnsubscribeAsync(string topic)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    _logger.LogWarning("Cannot unsubscribe. MQTT client is not connected.");
                    return;
                }

                var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
                    .WithTopicFilter(topic)
                    .Build();

                await _mqttClient.UnsubscribeAsync(unsubscribeOptions);
                _subscribedTopics.Remove(topic);
                _logger.LogInformation("Unsubscribed from topic {Topic}", topic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from topic {Topic}", topic);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync();
                _logger.LogInformation("MQTT client disconnected");
            }

            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _mqttClient?.Dispose();
            base.Dispose();
        }
    }

    public class MqttOptions
    {
        public string BrokerHost { get; set; } = "localhost";
        public int BrokerPort { get; set; } = 1883;
        public string ClientId { get; set; } = "VanadiumAPI";
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}

