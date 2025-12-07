using System.Text.Json;

namespace Services
{
    public class GatewayDataTranslator

    {
        public static GatewayData? Translate(string payload)
        {
            return JsonSerializer.Deserialize<GatewayData>(payload);
        }
    }
}