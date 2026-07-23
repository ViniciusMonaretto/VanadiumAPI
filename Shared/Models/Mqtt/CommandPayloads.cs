using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Models.Mqtt
{
    // cmd=2 SET_SENSOR_CONFIG params (also used as the per-item shape inside SetSensorConfigBulkParams)
    public class SetSensorConfigParams
    {
        [JsonPropertyName("sensor_id")]
        public int SensorId { get; set; }

        [JsonPropertyName("offset")]
        public float Offset { get; set; }

        [JsonPropertyName("gain")]
        public float Gain { get; set; }

        [JsonPropertyName("sampling_ms")]
        public int SamplingMs { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }

    // cmd=3 GET_SENSORS data
    public class GetSensorsData
    {
        [JsonPropertyName("sensors")]
        public List<SensorInfo> Sensors { get; set; } = new();
    }

    public class SensorInfo
    {
        [JsonPropertyName("sensor_id")]
        public int SensorId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("capabilities")]
        public JsonElement Capabilities { get; set; }

        [JsonPropertyName("config")]
        public SensorConfig Config { get; set; } = new();
    }

    public class SensorConfig
    {
        [JsonPropertyName("offset")]
        public float Offset { get; set; }

        [JsonPropertyName("gain")]
        public float Gain { get; set; }

        [JsonPropertyName("sampling_ms")]
        public int SamplingMs { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }

    // cmd=4 SET_SENSOR_CONFIG_BULK params
    public class SetSensorConfigBulkParams
    {
        [JsonPropertyName("sensors")]
        public List<SetSensorConfigParams> Sensors { get; set; } = new();
    }

    // cmd=4 error response data shape (status:"error")
    public class BulkConfigErrorData
    {
        [JsonPropertyName("errors")]
        public List<BulkConfigError> Errors { get; set; } = new();
    }

    public class BulkConfigError
    {
        [JsonPropertyName("sensor_id")]
        public int SensorId { get; set; }

        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
    }

    // cmd=5 GET_DEVICE_INFO data
    public class GetDeviceInfoData
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("firmware")]
        public string Firmware { get; set; } = string.Empty;

        [JsonPropertyName("serial")]
        public string Serial { get; set; } = string.Empty;
    }
}
