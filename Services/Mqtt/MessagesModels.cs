namespace Services
{

    public class MessageModel : EventArgs
    {
        public string Topic { get; set; } = string.Empty;
        public string GatewayId { get; set; } = string.Empty;
    }

     public class MqttOptions
    {
        public string BrokerHost { get; set; } = "localhost";
        public int BrokerPort { get; set; } = 1883;
        public string ClientId { get; set; } = "VanadiumAPI";
        public string? Username { get; set; }
        public string? Password { get; set; }
    }


    public class SensorDataMessageModel : MessageModel
    {
        public GatewayData GatewayData { get; set; } = new GatewayData();
    }

    public class SensorData
    {
        public float Value { get; set; }
        public bool Active { get; set; }
    }

    public class GatewayData
    {
        public DateTime Timestamp { get; set; }
        public List<SensorData> Sensors { get; set; }
    }


}