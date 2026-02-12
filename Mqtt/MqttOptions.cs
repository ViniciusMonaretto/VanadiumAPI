namespace VanadiumAPI.Mqtt
{
    public class MqttOptions
    {
        public string BrokerHost { get; set; } = "localhost";
        public int BrokerPort { get; set; } = 1883;
        public string ClientId { get; set; } = "VanadiumAPI";
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
