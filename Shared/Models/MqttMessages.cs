using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Models
{
    public class UnixTimestampConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException("Expected number for Unix timestamp");

            // timestamp may contain fractional seconds
            double timestamp = reader.GetDouble();

            long seconds = (long)Math.Floor(timestamp);
            double fractional = timestamp - seconds;

            // Convert to UTC
            var utc = DateTimeOffset.FromUnixTimeSeconds(seconds)
                                    .AddSeconds(fractional);

            // Convert to local server time
            return utc.ToLocalTime().DateTime;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            // Convert server local time -> UTC
            var utc = new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));

            // Write seconds (float ok, milliseconds ignored)
            writer.WriteNumberValue(utc.ToUnixTimeSeconds());
        }
    }

    public class MessageModel : EventArgs
    {
        public string Topic { get; set; } = string.Empty;
        public string GatewayId { get; set; } = string.Empty;
    }

    public class SensorDataMessageModel : MessageModel
    {
        public GatewayData GatewayData { get; set; } = new GatewayData();
    }

    public class SensorData
    {
        [JsonPropertyName("value")]
        public float Value { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; }
    }

    public class GatewayData
    {
        [JsonPropertyName("timestamp")]
        [JsonConverter(typeof(UnixTimestampConverter))]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("sensors")]
        public List<SensorData> Sensors { get; set; }
    }
}