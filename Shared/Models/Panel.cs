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
        PowerFactor = 6,
        Humidity = 7,
    }

    public class Panel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string GatewayId { get; set; }
        public Gateway Gateway { get; set; }
        public int GroupId { get; set; }
        public Group Group { get; set; }
        public string Index { get; set; }
        public string Color { get; set; }
        public float Gain { get; set; }
        public float Offset { get; set; }
        public int Multiplier { get; set; }
        public int SamplingMs { get; set; }
        public bool Enabled { get; set; } = true;

        /// <summary>True while an unconfirmed SET_SENSOR_CONFIG round-trip is outstanding or has failed.
        /// Such a panel was never accepted by the device; deleting it is a local-only purge.</summary>
        public bool Configuring { get; set; }
        public PanelType Type { get; set; }
        public ICollection<Alarm> Alarms { get; set; }
        public int DisplayedType { get; set; }
    }
}