using System.Text.Json;
using Shared.Models;

namespace VanadiumAPI.Mqtt
{
    public static class GatewayDataTranslator
    {
        public static GatewayData? Translate(string payload)
        {
            return JsonSerializer.Deserialize<GatewayData>(payload);
        }
    }
}
