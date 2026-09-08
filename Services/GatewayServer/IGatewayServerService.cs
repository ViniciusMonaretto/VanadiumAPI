using Shared.Models;
using VanadiumAPI.DTO;

namespace VanadiumAPI.Services
{
    public interface IGatewayServerService
    {
        Task InitializeStoreAsync();
        Task<(string? GatewayId, string? Error)> AddGatewayAsync(AddGatewayDto dto, int enterpriseId);
        Task<(bool Success, string? Error)> DeleteGatewayAsync(string gatewayId, int enterpriseId);

        /// <summary>Asks the device (GET_SENSORS) which sensors it has, persists them on the Gateway
        /// (setting IsSetup), refreshes the in-memory store and broadcasts the update.
        /// Never throws; returns the mapped sensor types on success.</summary>
        Task<(IReadOnlyList<PanelType>? AvailableSensors, string? Error)> RefreshGatewaySensorsAsync(
            string gatewayId, int enterpriseId, CancellationToken ct = default);
        Task AddGatewayToStoreAsync(int enterpriseId, string gatewayId);
        Task RemoveGatewayFromStoreAsync(int enterpriseId, string gatewayId);
        Task AddGatewaySystemInfoAsync(string gatewayId, SystemMessageModel systemData);
        Task UpdateGatewayLastActivityAsync(string gatewayId);
        Task<IReadOnlyDictionary<string, SystemMessageModel>> GetGatewayInfoByEnterpriseAsync(int enterpriseId);
        Task CheckGatewayHeartbeatTimeoutsAsync(TimeSpan timeout);
    }
}
