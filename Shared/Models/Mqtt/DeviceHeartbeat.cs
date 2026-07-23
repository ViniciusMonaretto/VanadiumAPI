using System.Text.Json.Serialization;

namespace Shared.Models.Mqtt
{
    /// <summary>Payload of iocloud/{deviceId}/heartbeat.</summary>
    public class DeviceHeartbeatPayload
    {
        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("ip")]
        public string? Ip { get; set; }

        [JsonPropertyName("uptime_ms")]
        public long UptimeMs { get; set; }
    }
}
