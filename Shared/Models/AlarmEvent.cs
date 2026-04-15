namespace Shared.Models
{
    public class AlarmEvent
    {
        public int Id { get; set; }
        public int AlarmId { get; set; }
        public Alarm Alarm { get; set; }
        /// <summary>Denormalized for queries and DTOs without loading <see cref="Alarm"/>.</summary>
        public int PanelId { get; set; }
        public DateTime EventTime { get; set; }
        /// <summary>Scaled reading value (<c>Gain * raw + Offset</c>) at the time the alarm fired.</summary>
        public float TriggeredValue { get; set; }
        /// <summary>Panel display name snapshot at event time.</summary>
        public string PanelName { get; set; } = string.Empty;
    }
}