namespace Shared.Models
{
    public class AlarmEvent
    {
        public int Id {get; set;}
        public int AlarmId {get; set;}
        public Alarm Alarm {get; set;}
        public DateTime EventTime {get; set;}
    }
}