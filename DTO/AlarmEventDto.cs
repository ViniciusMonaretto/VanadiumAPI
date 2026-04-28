using Shared.Models;

namespace VanadiumAPI.DTO
{
    public class AlarmEventDto
    {
        public int Id { get; set; }
        public int AlarmId { get; set; }
        public int PanelId { get; set; }
        /// <summary>Instant the alarm fired (JSON: <c>timestamp</c>).</summary>
        public DateTime? Timestamp { get; set; }
        /// <summary>Scaled sensor value when the alarm fired.</summary>
        public float Value { get; set; }
        /// <summary>Panel name at event time (or current name if legacy row).</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Severity of the alarm definition at fire time (from <see cref="Alarm.Severity"/>).</summary>
        public AlarmSeverity Severity { get; set; }

        /// <summary>
        /// Builds from a persisted <see cref="AlarmEvent"/>.
        /// Optional <paramref name="panelId"/> / <paramref name="panelName"/> help older rows or graphs where denormalized fields were not stored.
        /// Pass <paramref name="alarmSeverity"/> when <see cref="AlarmEvent.Alarm"/> is not loaded (e.g. hub broadcast right after insert).
        /// </summary>
        public AlarmEventDto(AlarmEvent alarmEvent, int? panelId = null, string? panelName = null, AlarmSeverity? alarmSeverity = null)
        {
            if (alarmEvent == null)
                throw new ArgumentNullException(nameof(alarmEvent));
            Id = alarmEvent.Id;
            AlarmId = alarmEvent.AlarmId;
            PanelId = alarmEvent.PanelId != 0
                ? alarmEvent.PanelId
                : (panelId ?? alarmEvent.Alarm?.PanelId ?? 0);
            Timestamp = alarmEvent.EventTime;
            Value = alarmEvent.TriggeredValue;
            Name = !string.IsNullOrEmpty(alarmEvent.PanelName)
                ? alarmEvent.PanelName
                : (!string.IsNullOrEmpty(panelName)
                    ? panelName
                    : alarmEvent.Alarm?.Panel?.Name ?? string.Empty);
            Severity = alarmSeverity ?? alarmEvent.Alarm?.Severity ?? AlarmSeverity.Warning;
        }

        public AlarmEventDto() { }
    }
}
