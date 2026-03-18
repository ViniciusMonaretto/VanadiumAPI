using Shared.Models;
using VanadiumAPI.DTO;

namespace VanadiumAPI.Services
{
    public interface IHubBroadcastService
    {
        Task BroadcastSensorData(SensorDataMessageModel sensorData);
        Task BroadcastPanelChange(PanelChangeAction action, PanelDto panelDto);
        Task BroadcastGatewaySystemInfo(int enterpriseId, SystemMessageModel systemData);
        void SubscribeToPanel(string connectionId, int userId, int panelId, string gatewayId);
        void UnsubscribeFromPanel(string connectionId, int panelId, string gatewayId);
        void SetUserPanels(string connectionId, int userId, int enterpriseId, IEnumerable<(int panelId, string gatewayId)> panelGatewayIds);
        int? GetConnectionEnterpriseId(string connectionId);
        void RemoveConnection(string connectionId);
        Task BroadcastGatewayAdded(int enterpriseId, string gatewayId);
        Task BroadcastGatewayRemoved(int enterpriseId, string gatewayId);
        void RemoveGateway(string gatewayId, IEnumerable<int> panelIds);
        Task BroadcastPanelAdded(int enterpriseId, PanelDto panelDto);
        Task BroadcastPanelRemoved(int enterpriseId, int panelId);
        void AddPanel(int enterpriseId, int panelId, string gatewayId);
        void RemovePanel(int panelId, string gatewayId);
        Task BroadcastGroupCreated(int enterpriseId, GroupDto groupDto);
        Task BroadcastGroupUpdated(int enterpriseId, GroupDto groupDto);
        Task BroadcastGroupRemoved(int enterpriseId, int groupId);
    }
}
