namespace Services
{
    public interface IMqttService
    {
        Task<bool> IsConnectedAsync();
        Task<bool> PublishAsync(string topic, string payload, bool retain = false);
        Task SubscribeAsync(string topic);
        Task UnsubscribeAsync(string topic);
        event EventHandler<MqttMessageReceivedEventArgs>? MessageReceived;
    }

    public class MqttMessageReceivedEventArgs : EventArgs
    {
        public string Topic { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public bool Retain { get; set; }
    }
}

