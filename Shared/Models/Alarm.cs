namespace Shared.Models
{
    public enum AlarmSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2
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
        public ICollection<AlarmEvent> AlarmEvents {get; set;}
    }
}