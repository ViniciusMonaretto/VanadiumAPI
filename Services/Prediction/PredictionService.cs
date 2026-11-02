using System.Text.Json;
using System.Threading.Channels;
using Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Mqtt;
using VanadiumAPI.SensorDataSaver;
using VanadiumAPI.Services.AlarmRegistry;

namespace VanadiumAPI.Services.Prediction
{
    /// <summary>
    /// Consumes IoCloud Predict (Orion) messages and turns each model fault start into an alarm
    /// event on the panel's anomaly alarm. <c>anomaly_ended</c> is only logged for now.
    /// </summary>
    public sealed class PredictionService : BackgroundService, IPredictionService
    {
        private readonly Channel<(string DeviceId, string Payload, bool IsState)> _channel =
            Channel.CreateUnbounded<(string, string, bool)>(new UnboundedChannelOptions { SingleReader = true });
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAlarmRegistryService _alarmRegistry;
        private readonly ISensorDataSaver _sensorDataSaver;
        private readonly ILogger<PredictionService> _logger;

        public PredictionService(
            IServiceScopeFactory scopeFactory,
            IAlarmRegistryService alarmRegistry,
            ISensorDataSaver sensorDataSaver,
            ILogger<PredictionService> logger)
        {
            _scopeFactory = scopeFactory;
            _alarmRegistry = alarmRegistry;
            _sensorDataSaver = sensorDataSaver;
            _logger = logger;
        }

        public void PushPrediction(string deviceId, string payload, bool isState)
        {
            _channel.Writer.TryWrite((deviceId, payload, isState));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var (deviceId, payload, isState) in _channel.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await HandleAsync(deviceId, payload, isState, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing prediction from {DeviceId}", deviceId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }

        /// <summary>Processes one message. Internal for tests; production goes through the channel.</summary>
        internal async Task HandleAsync(string deviceId, string? payload, bool isState, CancellationToken cancellationToken)
        {
            // An empty payload is how MQTT clears a retained message — nothing to do.
            if (string.IsNullOrWhiteSpace(payload))
                return;

            if (isState)
            {
                var state = Deserialize<DevicePredictStatePayload>(deviceId, payload);
                if (state == null || !IsSupported(deviceId, state.Version, state.DeviceId) || state.Active == null)
                    return;
                // A fault is open: record it in case its anomaly_started was missed while we were offline.
                await StartedAsync(deviceId, state.SensorId, state.Active.ZoneId, state.Active.Start, state.Active.Severity, cancellationToken);
                return;
            }

            var prediction = Deserialize<DevicePredictPayload>(deviceId, payload);
            if (prediction == null || !IsSupported(deviceId, prediction.Version, prediction.DeviceId))
                return;

            switch (prediction.Type)
            {
                case PredictEventTypes.AnomalyStarted:
                    await StartedAsync(deviceId, prediction.SensorId, prediction.ZoneId, prediction.Start, prediction.Severity, cancellationToken);
                    break;
                case PredictEventTypes.AnomalyEnded:
                    _logger.LogInformation("Prediction fault {ZoneId} ended on {DeviceId}/{SensorId} ({Start} → {End})",
                        prediction.ZoneId, deviceId, prediction.SensorId, prediction.Start, prediction.End);
                    break;
                default:
                    _logger.LogWarning("Unknown prediction type {Type} from {DeviceId}", prediction.Type, deviceId);
                    break;
            }
        }

        private async Task StartedAsync(string deviceId, int sensorId, string zoneId, DateTime start, string severity, CancellationToken cancellationToken)
        {
            Panel? panel;
            using (var scope = _scopeFactory.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                panel = await repo.GetPanelByGatewayAndIndexAsync(deviceId, sensorId.ToString());
            }
            if (panel == null)
            {
                _logger.LogDebug("Prediction for unknown panel: GatewayId={DeviceId}, Index={SensorId}", deviceId, sensorId);
                return;
            }

            // The reading the panel showed when the fault was reported; 0 if none arrived yet.
            var triggeredValue = _sensorDataSaver.GetLastPanelReading(panel.Id)?.Value ?? 0f;
            var created = await _alarmRegistry.ProcessPredictionStartedAsync(
                panel, zoneId, start, PredictEventTypes.ToSeverity(severity), triggeredValue, cancellationToken);
            if (created)
                _logger.LogInformation("Prediction fault {ZoneId} on panel {PanelId} → anomaly alarm event", zoneId, panel.Id);
        }

        private T? Deserialize<T>(string deviceId, string payload) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(payload);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid prediction payload from {DeviceId}: {Payload}", deviceId, payload);
                return null;
            }
        }

        private bool IsSupported(string topicDeviceId, int version, string payloadDeviceId)
        {
            if (version != PredictEventTypes.SupportedVersion)
            {
                _logger.LogWarning("Unsupported prediction version {Version} from {DeviceId}", version, topicDeviceId);
                return false;
            }
            if (!string.IsNullOrEmpty(payloadDeviceId) && payloadDeviceId != topicDeviceId)
                _logger.LogWarning("Prediction device_id {PayloadDeviceId} differs from topic {TopicDeviceId}; using the topic",
                    payloadDeviceId, topicDeviceId);
            return true;
        }
    }
}
