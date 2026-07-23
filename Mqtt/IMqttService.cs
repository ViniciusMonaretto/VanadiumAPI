using Shared.Models.Mqtt;

namespace VanadiumAPI.Mqtt
{
    public interface IMqttService
    {
        Task<bool> IsConnectedAsync();
        Task<bool> PublishAsync(string topic, string payload, bool retain = false);
        Task SubscribeAsync(string topic);
        Task UnsubscribeAsync(string topic);

        /// <summary>
        /// Publishes a command request to iocloud/{deviceId}/commands/request and awaits the
        /// correlated response on iocloud/{deviceId}/commands/response. Generates a unique request id.
        /// Throws TimeoutException if no matching response arrives within timeout.
        /// </summary>
        Task<CommandResponseEnvelope> SendCommandAsync(string deviceId, DeviceCommand cmd, object? @params, TimeSpan timeout, CancellationToken cancellationToken = default);
    }
}
