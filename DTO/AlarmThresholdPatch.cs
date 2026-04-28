using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Models;

namespace VanadiumAPI.DTO
{
    /// <summary>
    /// JSON: omit property = no change; <c>null</c> = remove all alarms of this kind;
    /// number = set threshold (severity defaults to <see cref="AlarmSeverity.Warning"/>);
    /// object <c>{{ "threshold": number, "severity": "Critical"|"Warning"|"Info" }}</c> for both.
    /// </summary>
    [JsonConverter(typeof(AlarmThresholdPatchJsonConverter))]
    public readonly struct AlarmThresholdPatch
    {
        public bool IsSpecified { get; init; }
        /// <summary>True when JSON was <c>null</c> — delete existing high/low alarms for that side.</summary>
        public bool ShouldClearAll { get; init; }
        public float Threshold { get; init; }
        public AlarmSeverity Severity { get; init; }
        /// <summary>True when JSON object included <c>severity</c> (updates should apply it).</summary>
        public bool SeveritySpecified { get; init; }
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
                    Threshold = reader.GetSingle(),
                    Severity = AlarmSeverity.Warning
                };
            if (reader.TokenType == JsonTokenType.StartObject)
                return ReadObject(ref reader);
            throw new JsonException($"Expected null, number, or object for alarm threshold patch, got {reader.TokenType}.");
        }

        private static AlarmThresholdPatch ReadObject(ref Utf8JsonReader reader)
        {
            float? threshold = null;
            var severity = AlarmSeverity.Warning;
            var severitySpecified = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    reader.Skip();
                    continue;
                }
                var name = reader.GetString();
                if (!reader.Read())
                    throw new JsonException("Unterminated alarm threshold object.");
                if (string.Equals(name, "threshold", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType != JsonTokenType.Number)
                        throw new JsonException("Alarm threshold object requires numeric \"threshold\".");
                    threshold = reader.GetSingle();
                }
                else if (string.Equals(name, "severity", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType != JsonTokenType.String)
                        throw new JsonException("Alarm \"severity\" must be a string.");
                    if (!Enum.TryParse(reader.GetString(), ignoreCase: true, out AlarmSeverity s) || !Enum.IsDefined(typeof(AlarmSeverity), s))
                        throw new JsonException($"Invalid alarm severity. Use one of: {string.Join(", ", Enum.GetNames(typeof(AlarmSeverity)))}.");
                    severity = s;
                    severitySpecified = true;
                }
                else
                    reader.Skip();
            }
            if (threshold == null)
                throw new JsonException("Alarm threshold object requires a \"threshold\" number.");
            return new AlarmThresholdPatch
            {
                IsSpecified = true,
                ShouldClearAll = false,
                Threshold = threshold.Value,
                Severity = severity,
                SeveritySpecified = severitySpecified
            };
        }

        public override void Write(Utf8JsonWriter writer, AlarmThresholdPatch value, JsonSerializerOptions options)
        {
            if (!value.IsSpecified || value.ShouldClearAll)
                writer.WriteNullValue();
            else if (value.SeveritySpecified)
            {
                writer.WriteStartObject();
                writer.WriteNumber("threshold", value.Threshold);
                writer.WriteString("severity", value.Severity.ToString());
                writer.WriteEndObject();
            }
            else
                writer.WriteNumberValue(value.Threshold);
        }
    }
}
