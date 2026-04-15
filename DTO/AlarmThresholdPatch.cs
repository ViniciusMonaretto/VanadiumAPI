using System.Text.Json;
using System.Text.Json.Serialization;

namespace VanadiumAPI.DTO
{
    /// <summary>
    /// JSON: omit property = no change; <c>null</c> = remove all alarms of this kind; number = add a new alarm with that threshold.
    /// </summary>
    [JsonConverter(typeof(AlarmThresholdPatchJsonConverter))]
    public readonly struct AlarmThresholdPatch
    {
        public bool IsSpecified { get; init; }
        /// <summary>True when JSON was <c>null</c> — delete existing high/low alarms for that side.</summary>
        public bool ShouldClearAll { get; init; }
        public float Threshold { get; init; }
        public bool HasThreshold => IsSpecified && !ShouldClearAll;
    }

    public sealed class AlarmThresholdPatchJsonConverter : JsonConverter<AlarmThresholdPatch>
    {
        public override AlarmThresholdPatch Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return new AlarmThresholdPatch { IsSpecified = true, ShouldClearAll = true };
            if (reader.TokenType == JsonTokenType.Number)
                return new AlarmThresholdPatch
                {
                    IsSpecified = true,
                    ShouldClearAll = false,
                    Threshold = reader.GetSingle()
                };
            throw new JsonException($"Expected null or number for alarm threshold patch, got {reader.TokenType}.");
        }

        public override void Write(Utf8JsonWriter writer, AlarmThresholdPatch value, JsonSerializerOptions options)
        {
            if (!value.IsSpecified || value.ShouldClearAll)
                writer.WriteNullValue();
            else
                writer.WriteNumberValue(value.Threshold);
        }
    }
}
