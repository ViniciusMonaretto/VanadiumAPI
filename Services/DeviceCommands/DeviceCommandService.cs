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
