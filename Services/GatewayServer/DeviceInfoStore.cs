using System.Collections.Concurrent;
using Shared.Models.Mqtt;

namespace VanadiumAPI.Services
{
    public class DeviceInfoStore : IDeviceInfoStore
    {
        private readonly ConcurrentDictionary<string, GetDeviceInfoData> _deviceInfo = new();
        private readonly ConcurrentDictionary<string, List<SensorInfo>> _sensorCapabilities = new();

        public void UpdateDeviceInfo(string deviceId, GetDeviceInfoData info) => _deviceInfo[deviceId] = info;

        public void UpdateSensorCapabilities(string deviceId, List<SensorInfo> sensors) => _sensorCapabilities[deviceId] = sensors;

        public GetDeviceInfoData? GetDeviceInfo(string deviceId) =>
            _deviceInfo.TryGetValue(deviceId, out var value) ? value : null;

        public IReadOnlyList<SensorInfo>? GetSensorCapabilities(string deviceId) =>
            _sensorCapabilities.TryGetValue(deviceId, out var value) ? value : null;
    }
}
