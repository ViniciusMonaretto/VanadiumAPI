using Shared.Models.Mqtt;

namespace VanadiumAPI.Services.DeviceCommands
{
    public interface IDeviceCommandService
    {
        Task<(bool Ok, string? Error)> RebootAsync(string deviceId, CancellationToken ct = default);
        Task<(GetSensorsData? Data, string? Error)> GetSensorsAsync(string deviceId, CancellationToken ct = default);
        Task<(bool Ok, List<BulkConfigError>? Errors, string? Error)> SetSensorConfigBulkAsync(string deviceId, List<SetSensorConfigParams> sensors, CancellationToken ct = default);
        Task<(GetDeviceInfoData? Data, string? Error)> GetDeviceInfoAsync(string deviceId, CancellationToken ct = default);
    }
}
