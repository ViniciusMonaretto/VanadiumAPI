using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Models.Mqtt
{
    public enum DeviceCommand
    {
        Reboot = 1,
        SetSensorConfig = 2,
        GetSensors = 3,
        SetSensorConfigBulk = 4,
        GetDeviceInfo = 5,
    }

    /// <summary>Host -> device, published to iocloud/{deviceId}/commands/request.</summary>
    public class CommandRequestEnvelope
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("cmd")]
        public int Cmd { get; set; }

        [JsonPropertyName("params")]
        public object? Params { get; set; }
    }

    /// <summary>Device -> host, received on iocloud/{deviceId}/commands/response.</summary>
    public class CommandResponseEnvelope
    {
        [JsonPropertyName("cmd")]
        public int Cmd { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("data")]
        public JsonElement Data { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }
}
