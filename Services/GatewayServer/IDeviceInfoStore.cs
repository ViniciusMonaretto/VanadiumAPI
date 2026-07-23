using Shared.Models.Mqtt;

namespace VanadiumAPI.Services
{
    /// <summary>
    /// In-memory cache of device-reported info/capabilities (GET_DEVICE_INFO / GET_SENSORS results).
    /// Not persisted - repopulated on the next command round-trip after a restart.
    /// </summary>
    public interface IDeviceInfoStore
    {
        void UpdateDeviceInfo(string deviceId, GetDeviceInfoData info);
        void UpdateSensorCapabilities(string deviceId, List<SensorInfo> sensors);
        GetDeviceInfoData? GetDeviceInfo(string deviceId);
        IReadOnlyList<SensorInfo>? GetSensorCapabilities(string deviceId);
    }
}
