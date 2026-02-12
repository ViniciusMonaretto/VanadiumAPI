using System.Threading.Channels;
using MQTTnet;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Models;
using VanadiumAPI.SensorDataSaver;
using VanadiumAPI.Services;

namespace VanadiumAPI.Mqtt
{
    public class MqttService : BackgroundService, IMqttService
    {
        private readonly IMqttClient _mqttClient;
        private readonly MqttClientOptions _mqttClientOptions;
        private readonly ILogger<MqttService> _logger;
        private readonly ISensorDataSaver _sensorDataSaver;
        private readonly IPanelBroadcastService _broadcastService;
        private readonly HashSet<string> _subscribedTopics = new();
        private readonly Channel<(string Topic, string Payload)> _messageQueue = Channel.CreateUnbounded<(string, string)>();

        public MqttService(
            IOptions<MqttOptions> mqttOptions,
            ILogger<MqttService> logger,
            ISensorDataSaver sensorDataSaver,
            IPanelBroadcastService broadcastService)
        {
            _logger = logger;
            _sensorDataSaver = sensorDataSaver;
            _broadcastService = broadcastService;

            var mqttFactory = new MqttClientFactory();
            _mqttClient = mqttFactory.CreateMqttClient();

            var mqtt = mqttOptions.Value;
            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(mqtt.BrokerHost, mqtt.BrokerPort)
                .WithClientId(mqtt.ClientId)
                .WithCleanSession();
            if (!string.IsNullOrEmpty(mqtt.Username))
                optionsBuilder.WithCredentials(mqtt.Username, mqtt.Password);
            _mqttClientOptions = optionsBuilder.Build();

            _mqttClient.ApplicationMessageReceivedAsync += HandleMessageReceived;
            _mqttClient.ConnectedAsync += HandleConnected;
            _mqttClient.DisconnectedAsync += HandleDisconnected;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await ConnectAsync(stoppingToken);

            var processTask = ProcessMessageQueueAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_mqttClient.IsConnected)
                {
                    _logger.LogWarning("MQTT client disconnected. Attempting to reconnect...");
                    await ConnectAsync(stoppingToken);
                }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            _messageQueue.Writer.Complete();
            await processTask;
        }

        private Task HandleMessageReceived(MqttApplicationMessageReceivedEventArgs args)
        {
            var topic = args.ApplicationMessage.Topic;
            var payload = args.ApplicationMessage.ConvertPayloadToString();
            _messageQueue.Writer.TryWrite((topic, payload));
            return Task.CompletedTask;
        }

        private async Task ProcessMessageQueueAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var (topic, payload) in _messageQueue.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        ProcessOneMessage(topic, payload);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing MQTT message from queue");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Esperado ao encerrar o serviço
            }
        }

        private void ProcessOneMessage(string topic, string payload)
        {
            var parts = topic.Split('/');
            if (parts.Length < 5)
            {
                _logger.LogWarning("Invalid topic format: {Topic}", topic);
                return;
            }

            var gatewayId = parts[2];
            var command = parts[4];

            if (command == "report")
            {
                var gatewayData = GatewayDataTranslator.Translate(payload);
                if (gatewayData == null)
                {
                    _logger.LogError("Invalid gateway data: {Payload}", payload);
                    return;
                }

                var msg = new SensorDataMessageModel
                {
                    Topic = topic,
                    GatewayId = gatewayId,
                    GatewayData = gatewayData
                };
                _sensorDataSaver.PushSensorData(msg);
                _ = _broadcastService.BroadcastSensorData(msg);
            }
            else
            {
                _logger.LogWarning("Unknown command: {Command}", command);
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

        public async Task SubscribeAsync(string topic)
        {
            if (!_mqttClient.IsConnected) return;
            var options = new MqttClientSubscribeOptionsBuilder().WithTopicFilter(topic).Build();
            var result = await _mqttClient.SubscribeAsync(options);
            if (result.Items.Any(x => x.ResultCode == MqttClientSubscribeResultCode.GrantedQoS0 ||
                                     x.ResultCode == MqttClientSubscribeResultCode.GrantedQoS1 ||
                                     x.ResultCode == MqttClientSubscribeResultCode.GrantedQoS2))
                _subscribedTopics.Add(topic);
        }

        public async Task UnsubscribeAsync(string topic)
        {
            if (!_mqttClient.IsConnected) return;
            var options = new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(topic).Build();
            await _mqttClient.UnsubscribeAsync(options);
            _subscribedTopics.Remove(topic);
        }

        public Task<bool> IsConnectedAsync() => Task.FromResult(_mqttClient.IsConnected);

        public async Task<bool> PublishAsync(string topic, string payload, bool retain = false)
        {
            return (await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(retain)
                .Build())).IsSuccess;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_mqttClient.IsConnected)
                await _mqttClient.DisconnectAsync();
            await base.StopAsync(cancellationToken);
        }
    }
}
