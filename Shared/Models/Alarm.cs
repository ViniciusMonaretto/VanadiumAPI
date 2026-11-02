namespace Shared.Models
{
    public enum AlarmSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }

    public enum AlarmKind
    {
        /// <summary>Fires when a reading crosses <see cref="Alarm.Threshold"/> (min/max alarms).</summary>
        Threshold = 0,
        /// <summary>Fires when IoCloud Predict (Orion) reports a model fault on
        /// <c>iocloud/{deviceId}/predict</c>. Threshold/IsGreaterThan are unused.</summary>
        Anomaly = 1
    }

    public class Alarm
    {
        public int Id {get; set;}
        public int PanelId {get; set;}
        public Panel Panel {get; set;}
        public float Threshold {get; set;}
        public bool IsGreaterThan {get; set;}
        /// <summary>How serious this alarm is when it fires (defaults to <see cref="AlarmSeverity.Warning"/>).</summary>
        public AlarmSeverity Severity { get; set; } = AlarmSeverity.Warning;
        public AlarmKind Kind { get; set; } = AlarmKind.Threshold;
        public ICollection<AlarmEvent> AlarmEvents {get; set;}
    }
}