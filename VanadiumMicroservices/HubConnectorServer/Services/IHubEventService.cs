using Shared.Models;

namespace API.Services
{
    public interface IHubEventService
    {
        Task SendSensorDataReceived(SensorDataMessageModel sensorData, CancellationToken cancellationToken);
        Task SendPanelChangeReceived(PanelChangeMessage panelChange, CancellationToken cancellationToken);
        void SubscribeToPanel(int panelId);
        void UnsubscribeFromPanel(int panelId);
        void SubscribeToAllPanels();
        bool IsSubscribedToPanel(int panelId);
        bool IsSubscribedToAll();
    }
}
