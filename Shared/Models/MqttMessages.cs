using Shared.Models.Mqtt;

namespace Shared.Models
{
    public class MessageModel : EventArgs
    {
        public string Topic { get; set; } = string.Empty;
        public string GatewayId { get; set; } = string.Empty;
    }

    public class SensorDataMessageModel : MessageModel
    {
        public DeviceTelemetryPayload Telemetry { get; set; } = new DeviceTelemetryPayload();
    }

    public class SystemMessageModel : MessageModel
    {
        public string GatewayId { get; set; } = string.Empty;
        public DateTime? Uptime { get; set; }
        public string? IpAddress { get; set; }
        public DateTime? LastActivity { get; set; }
        public bool IsConnected { get; set; }
    }
}