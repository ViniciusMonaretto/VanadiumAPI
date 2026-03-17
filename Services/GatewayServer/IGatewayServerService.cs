using Shared.Models;
using VanadiumAPI.DTO;

namespace VanadiumAPI.Services
{
    public interface IGatewayServerService
    {
        Task InitializeStoreAsync();
        Task<(string? GatewayId, string? Error)> AddGatewayAsync(AddGatewayDto dto, int enterpriseId);
        Task<(bool Success, string? Error)> DeleteGatewayAsync(string gatewayId, int enterpriseId);
        Task AddGatewayToStoreAsync(int enterpriseId, string gatewayId);
        Task RemoveGatewayFromStoreAsync(int enterpriseId, string gatewayId);
        Task AddGatewaySystemInfoAsync(string gatewayId, SystemMessageModel systemData);
        Task<IReadOnlyDictionary<string, SystemMessageModel>> GetGatewayInfoByEnterpriseAsync(int enterpriseId);
    }
}
