using System.Text.Json.Serialization;

namespace Shared.Models.Mqtt
{
    /// <summary>
    /// Payload of iocloud/{deviceId}/predict, published by IoCloud Predict (Orion) when its model
    /// detects a fault on a sensor (contract: Orion docs/architecture.md, "Prediction contract").
    /// </summary>
    public class DevicePredictPayload
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>Sensor on the device; with the device id it identifies the panel (Panel.Index).</summary>
        [JsonPropertyName("sensor_id")]
        public int SensorId { get; set; }

        /// <summary>Same id on <c>anomaly_started</c> and <c>anomaly_ended</c>; used to deduplicate.</summary>
        [JsonPropertyName("zone_id")]
        public string ZoneId { get; set; } = string.Empty;

        /// <summary><see cref="PredictEventTypes.AnomalyStarted"/> or <see cref="PredictEventTypes.AnomalyEnded"/>.</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;

        /// <summary>First sample of the fault (UTC).</summary>
        [JsonPropertyName("start")]
        public DateTime Start { get; set; }

        /// <summary>Last faulty sample (UTC); only on <c>anomaly_ended</c>.</summary>
        [JsonPropertyName("end")]
        public DateTime? End { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Retained payload of iocloud/{deviceId}/predict/state: the fault currently open on the sensor,
    /// or null. Lets a consumer that was offline catch a missed <c>anomaly_started</c>.
    /// </summary>
    public class DevicePredictStatePayload
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("sensor_id")]
        public int SensorId { get; set; }

        [JsonPropertyName("active")]
        public PredictActiveFault? Active { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }

    public class PredictActiveFault
    {
        [JsonPropertyName("zone_id")]
        public string ZoneId { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;

        [JsonPropertyName("start")]
        public DateTime Start { get; set; }
    }

    public static class PredictEventTypes
    {
        public const int SupportedVersion = 1;
        public const string AnomalyStarted = "anomaly_started";
        public const string AnomalyEnded = "anomaly_ended";

        /// <summary>Prediction severity string → alarm severity (unknown → Warning).</summary>
        public static AlarmSeverity ToSeverity(string? severity) => severity switch
        {
            "info" => AlarmSeverity.Info,
            "critical" => AlarmSeverity.Critical,
            _ => AlarmSeverity.Warning,
        };
    }
}
