using System.Text.Json.Serialization;

namespace Shared.Models.Mqtt
{
    /// <summary>Payload of iocloud/{deviceId}/telemetry.</summary>
    public class DeviceTelemetryPayload
    {
        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("readings")]
        public List<SensorReading> Readings { get; set; } = new();
    }

    public class SensorReading
    {
        [JsonPropertyName("sensor_id")]
        public int SensorId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public float Value { get; set; }
    }
}
