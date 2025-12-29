namespace Shared.Models
{
    public enum PanelType
    {
        Temperature = 0,
        Pressure = 1,
        Flow = 2,
        Power = 3,
        Current = 4,
        Voltage = 5,
        Frequency = 6,
        PowerFactor = 7,
    }

    public class Panel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string GatewayId { get; set; }
        public int GroupId { get; set; }
        public Group Group { get; set; }
        public string Index { get; set; }
        public string Color { get; set; }
        public float Gain { get; set; }
        public float Offset { get; set; }
        public int Multiplier { get; set; }
        public PanelType Type { get; set; }
        public ICollection<Alarm> Alarms { get; set; }
    }
}