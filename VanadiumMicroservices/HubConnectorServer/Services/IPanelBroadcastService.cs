using Shared.Models;

namespace API.Services
{
    public interface IPanelBroadcastService
    {
        Task BroadcastSensorData(SensorDataMessageModel sensorData);
        Task BroadcastPanelChange(PanelChangeMessage panelChange);
        void SubscribeToPanel(string connectionId, int userId, int panelId, string gatewayId);
        void UnsubscribeFromPanel(string connectionId, int panelId, string gatewayId);
        void SetUserPanels(string connectionId, int userId, IEnumerable<(int panelId, string gatewayId)> panelGatewayIds);
        void RemoveConnection(string connectionId);
    }
}