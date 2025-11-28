namespace Models
{
    public class Panel
    {
        public int Id {get; set;}
        public string Name {get; set;}
        public string GatewayId {get; set;}
        public int GroupId {get; set;}
        public Group Group {get; set;}
        public string Index {get; set;}
        public string Color {get; set;}
        public float Gain {get; set;}
        public float Offset {get; set;}
        public ICollection<Alarm> Alarms {get; set;}
    }
}