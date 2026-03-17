using System.Text.Json;
using Shared.Models;

namespace VanadiumAPI.Mqtt
{
    /// <summary>System command in MQTT command response has command_index 2.</summary>
    public static class GatewayDataTranslator
    {
        public const int SystemCommandIndex = 2;

        public static object? Translate(string command, string payload)
        {
            if (command == "report")
            {
                return JsonSerializer.Deserialize<GatewayData>(payload);
            }
            if (command == "command")
            {
                return TryTranslateSystemCommandResponse(payload);
            }
            return null;
        }

        /// <summary>Parses command response payload; returns SystemMessageModel when command_index is 2 (system).</summary>
        private static SystemMessageModel? TryTranslateSystemCommandResponse(string payload)
        {
            var response = JsonSerializer.Deserialize<CommandResponsePayload>(payload);
            if (response == null || response.CommandIndex != SystemCommandIndex)
                return null;
            return new SystemMessageModel
            {
                GatewayId = response.DeviceId,
                IpAddress = response.IpAddress,
                Uptime = DateTime.UtcNow.AddSeconds(-response.UptimeSeconds),
                IsConnected = true
            };
        }
    }
}
