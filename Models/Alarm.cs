namespace Models
{
    public class Alarm
    {
        public int Id {get; set;}
        public string PanelId {get; set;}
        public Panel Panel {get; set;}
        public float Threshold {get; set;}
        public bool IsGreaterThan {get; set;}
        public ICollection<AlarmEvent> AlarmEvents {get; set;}
    }
}