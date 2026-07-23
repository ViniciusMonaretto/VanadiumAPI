using System.Text.Json;

namespace Shared.Models.Mqtt
{
    /// <summary>
    /// Generic/untyped payload of iocloud/{deviceId}/events. No fixed schema yet - stores the
    /// raw JSON body so a typed model can replace RawPayload later without touching dispatch code.
    /// </summary>
    public class DeviceEventMessage
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public JsonElement RawPayload { get; set; }
        public DateTime ReceivedAt { get; set; }
    }
}
