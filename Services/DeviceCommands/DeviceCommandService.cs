using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Models.Mqtt;
using VanadiumAPI.Mqtt;

namespace VanadiumAPI.Services.DeviceCommands
{
    public class DeviceCommandService : IDeviceCommandService
    {
        private readonly IMqttService _mqttService;
        private readonly IDeviceInfoStore _deviceInfoStore;
        private readonly ILogger<DeviceCommandService> _logger;
        private readonly TimeSpan _defaultTimeout;

        public DeviceCommandService(
            IMqttService mqttService,
            IOptions<MqttOptions> mqttOptions,
            IDeviceInfoStore deviceInfoStore,
            ILogger<DeviceCommandService> logger)
        {
            _mqttService = mqttService;
            _deviceInfoStore = deviceInfoStore;
            _logger = logger;
            _defaultTimeout = TimeSpan.FromSeconds(mqttOptions.Value.DefaultCommandTimeoutSeconds);
        }

        public async Task<(bool Ok, string? Error)> RebootAsync(string deviceId, CancellationToken ct = default)
        {
            try
            {
                var response = await _mqttService.SendCommandAsync(deviceId, DeviceCommand.Reboot, null, _defaultTimeout, ct);
                return response.Status == "ok" ? (true, null) : (false, $"Device returned status '{response.Status}'");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("REBOOT command timed out for device {DeviceId}", deviceId);
                return (false, "Timeout aguardando resposta do dispositivo");
            }
        }

        public async Task<(GetSensorsData? Data, string? Error)> GetSensorsAsync(string deviceId, CancellationToken ct = default)
        {
            try
            {
                var response = await _mqttService.SendCommandAsync(deviceId, DeviceCommand.GetSensors, null, _defaultTimeout, ct);
                if (response.Status != "ok")
                    return (null, $"Device returned status '{response.Status}'");

                var data = response.Data.Deserialize<GetSensorsData>();
                if (data != null)
                    _deviceInfoStore.UpdateSensorCapabilities(deviceId, data.Sensors);
                return (data, null);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("GET_SENSORS command timed out for device {DeviceId}", deviceId);
                return (null, "Timeout aguardando resposta do dispositivo");
            }
        }

        public async Task<(bool Ok, string? Error)> SetSensorConfigAsync(string deviceId, SetSensorConfigParams config, CancellationToken ct = default)
        {
            try
            {
                var response = await _mqttService.SendCommandAsync(deviceId, DeviceCommand.SetSensorConfig, config, _defaultTimeout, ct);
                if (response.Status == "ok")
                    return (true, null);

                // cmd=2 reuses the cmd=4 error shape: {"errors":[{"sensor_id":n,"error":"..."}]}
                try
                {
                    if (response.Data.ValueKind == JsonValueKind.Object)
                    {
                        var errorData = response.Data.Deserialize<BulkConfigErrorData>();
                        if (errorData != null && errorData.Errors.Count > 0)
                            return (false, $"Dispositivo recusou o sensor {errorData.Errors[0].SensorId}: {errorData.Errors[0].Error}");
                    }
                }
                catch (JsonException)
                {
                    // Device sent a shape we don't model; fall through to the generic status message.
                }

                return (false, $"Dispositivo retornou status '{response.Status}'");
            }
            catch (TimeoutException)
            {
                // Also raised when the caller's token is cancelled (see MqttService.SendCommandAsync).
                _logger.LogWarning("SET_SENSOR_CONFIG timed out for device {DeviceId} sensor {SensorId}", deviceId, config.SensorId);
                return (false, "Timeout aguardando resposta do dispositivo");
            }
            catch (InvalidOperationException ex)
            {
                // PublishAsync reported failure.
                _logger.LogWarning(ex, "SET_SENSOR_CONFIG could not be published for device {DeviceId}", deviceId);
                return (false, "Não foi possível enviar o comando ao dispositivo");
            }
            catch (Exception ex)
            {
                // Broker down: MQTTnet throws from PublishAsync. Callers are detached tasks, so nothing may escape.
                _logger.LogError(ex, "SET_SENSOR_CONFIG failed for device {DeviceId}", deviceId);
                return (false, "Erro ao comunicar com o dispositivo");
            }
        }

        public async Task<(bool Ok, List<BulkConfigError>? Errors, string? Error)> SetSensorConfigBulkAsync(string deviceId, List<SetSensorConfigParams> sensors, CancellationToken ct = default)
        {
            try
            {
                var @params = new SetSensorConfigBulkParams { Sensors = sensors };
                var response = await _mqttService.SendCommandAsync(deviceId, DeviceCommand.SetSensorConfigBulk, @params, _defaultTimeout, ct);
                if (response.Status == "ok")
                    return (true, null, null);

                var errorData = response.Data.Deserialize<BulkConfigErrorData>();
                if (errorData != null && errorData.Errors.Count > 0)
                    return (false, errorData.Errors, null);

                return (false, null, $"Device returned status '{response.Status}'");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("SET_SENSOR_CONFIG_BULK command timed out for device {DeviceId}", deviceId);
                return (false, null, "Timeout aguardando resposta do dispositivo");
            }
        }

        public async Task<(GetDeviceInfoData? Data, string? Error)> GetDeviceInfoAsync(string deviceId, CancellationToken ct = default)
        {
            try
            {
                var response = await _mqttService.SendCommandAsync(deviceId, DeviceCommand.GetDeviceInfo, null, _defaultTimeout, ct);
                if (response.Status != "ok")
                    return (null, $"Device returned status '{response.Status}'");

                var data = response.Data.Deserialize<GetDeviceInfoData>();
                if (data != null)
                    _deviceInfoStore.UpdateDeviceInfo(deviceId, data);
                return (data, null);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("GET_DEVICE_INFO command timed out for device {DeviceId}", deviceId);
                return (null, "Timeout aguardando resposta do dispositivo");
            }
        }
    }
}
