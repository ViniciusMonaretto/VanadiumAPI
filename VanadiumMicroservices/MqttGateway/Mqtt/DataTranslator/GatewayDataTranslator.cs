using System.Text.Json;
using Shared.Models;

namespace Mqtt
{
    public class GatewayDataTranslator

    {
        public static GatewayData? Translate(string payload)
        {
            return JsonSerializer.Deserialize<GatewayData>(payload);
        }
    }
}