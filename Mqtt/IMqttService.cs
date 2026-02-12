namespace VanadiumAPI.Mqtt
{
    public interface IMqttService
    {
        Task<bool> IsConnectedAsync();
        Task<bool> PublishAsync(string topic, string payload, bool retain = false);
        Task SubscribeAsync(string topic);
        Task UnsubscribeAsync(string topic);
    }
}
