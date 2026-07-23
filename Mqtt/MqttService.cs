using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MQTTnet;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Models;
using Shared.Models.Mqtt;
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
        private readonly IHubBroadcastService _broadcastService;
        private readonly IGatewayServerService _gatewayServer;
        private readonly HashSet<string> _subscribedTopics = new();
        private readonly Channel<(string Topic, string Payload)> _messageQueue = Channel.CreateUnbounded<(string, string)>();

        private static int _nextRequestId;
        private readonly ConcurrentDictionary<int, PendingCommand> _pendingCommands = new();

        private sealed class PendingCommand
        {
            public required string DeviceId { get; init; }
            public required int Cmd { get; init; }
            public required TaskCompletionSource<CommandResponseEnvelope> Tcs { get; init; }
            public CancellationTokenRegistration TimeoutRegistration { get; set; }
        }

        public MqttService(
            IOptions<MqttOptions> mqttOptions,
            ILogger<MqttService> logger,
            ISensorDataSaver sensorDataSaver,
            IHubBroadcastService broadcastService,
            IGatewayServerService gatewayServer)
        {
            _logger = logger;
            _sensorDataSaver = sensorDataSaver;
            _broadcastService = broadcastService;
            _gatewayServer = gatewayServer;

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
            if (parts.Length < 3 || parts[0] != "iocloud")
            {
                _logger.LogWarning("Invalid topic format: {Topic}", topic);
                return;
            }

            var deviceId = parts[1];
            var suffix = string.Join('/', parts.Skip(2));

            switch (suffix)
            {
                case "heartbeat":
                    HandleHeartbeat(deviceId, topic, payload);
                    break;
                case "telemetry":
                    HandleTelemetry(deviceId, topic, payload);
                    break;
                case "events":
                    HandleEvent(deviceId, topic, payload);
                    break;
                case "commands/response":
                    HandleCommandResponse(deviceId, payload);
                    break;
                default:
                    _logger.LogWarning("Unknown topic suffix: {Suffix} (topic {Topic})", suffix, topic);
                    break;
            }
        }

        private void HandleHeartbeat(string deviceId, string topic, string payload)
        {
            DeviceHeartbeatPayload? heartbeat;
            try
            {
                heartbeat = JsonSerializer.Deserialize<DeviceHeartbeatPayload>(payload);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid heartbeat payload for {DeviceId}", deviceId);
                return;
            }
            if (heartbeat == null) return;

            var systemData = new SystemMessageModel
            {
                Topic = topic,
                GatewayId = deviceId,
                IpAddress = heartbeat.Ip,
                Uptime = DateTime.UtcNow.AddMilliseconds(-heartbeat.UptimeMs),
                IsConnected = true,
            };
            _ = _gatewayServer.AddGatewaySystemInfoAsync(deviceId, systemData);
        }

        private void HandleTelemetry(string deviceId, string topic, string payload)
        {
            DeviceTelemetryPayload? telemetry;
            try
            {
                telemetry = JsonSerializer.Deserialize<DeviceTelemetryPayload>(payload);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid telemetry payload for {DeviceId}", deviceId);
                return;
            }
            if (telemetry == null)
            {
                _logger.LogError("Invalid telemetry data: {Payload}", payload);
                return;
            }

            var msg = new SensorDataMessageModel
            {
                Topic = topic,
                GatewayId = deviceId,
                Telemetry = telemetry
            };
            _sensorDataSaver.PushSensorData(msg);
            _ = _broadcastService.BroadcastSensorData(msg);
            _ = _gatewayServer.UpdateGatewayLastActivityAsync(deviceId);
        }

        private void HandleEvent(string deviceId, string topic, string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var evt = new DeviceEventMessage
                {
                    DeviceId = deviceId,
                    Topic = topic,
                    RawPayload = doc.RootElement.Clone(),
                    ReceivedAt = DateTime.UtcNow
                };
                _logger.LogDebug("Device event received: {DeviceId} {Payload}", evt.DeviceId, payload);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid event payload for {DeviceId}: {Payload}", deviceId, payload);
            }
        }

        private void HandleCommandResponse(string deviceId, string payload)
        {
            CommandResponseEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<CommandResponseEnvelope>(payload);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid command response for {DeviceId}", deviceId);
                return;
            }
            if (envelope == null) return;

            if (_pendingCommands.TryRemove(envelope.Id, out var pending))
            {
                if (pending.Cmd != envelope.Cmd)
                    _logger.LogWarning("Command response cmd mismatch for id {Id}: expected {Expected}, got {Actual}", envelope.Id, pending.Cmd, envelope.Cmd);
                pending.Tcs.TrySetResult(envelope);
            }
            else
            {
                _logger.LogWarning("Unexpected/late command response from {DeviceId}: id={Id} cmd={Cmd}", deviceId, envelope.Id, envelope.Cmd);
            }
        }

        public async Task<CommandResponseEnvelope> SendCommandAsync(string deviceId, DeviceCommand cmd, object? @params, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var id = Interlocked.Increment(ref _nextRequestId);
            var tcs = new TaskCompletionSource<CommandResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingCommand { DeviceId = deviceId, Cmd = (int)cmd, Tcs = tcs };

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            pending.TimeoutRegistration = cts.Token.Register(() =>
            {
                if (_pendingCommands.TryRemove(id, out _))
                    tcs.TrySetException(new TimeoutException($"No response for cmd={cmd} id={id} device={deviceId} within {timeout}"));
            });

            _pendingCommands[id] = pending;

            try
            {
                var envelope = new CommandRequestEnvelope { Id = id, Cmd = (int)cmd, Params = @params };
                var options = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
                var payload = JsonSerializer.Serialize(envelope, options);

                var published = await PublishAsync($"iocloud/{deviceId}/commands/request", payload);
                if (!published)
                    throw new InvalidOperationException($"Failed to publish command {cmd} to device {deviceId}");

                return await tcs.Task;
            }
            finally
            {
                _pendingCommands.TryRemove(id, out _);
                pending.TimeoutRegistration.Dispose();
                cts.Dispose();
            }
        }

        private async Task HandleConnected(MqttClientConnectedEventArgs args)
        {
            await SubscribeAsync("iocloud/+/heartbeat");
            await SubscribeAsync("iocloud/+/telemetry");
            await SubscribeAsync("iocloud/+/events");
            await SubscribeAsync("iocloud/+/commands/response");
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
