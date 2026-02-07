using Shared.Models;
using HubConnectorServer.DTO;

namespace API.Services
{
    public interface ISensorInfoService
    {
        Task<IEnumerable<Panel>> GetAllPanelsAsync();
        Task<Panel?> GetPanelByIdAsync(int id);
        Task<IEnumerable<Alarm>> GetAllAlarmsAsync();
        Task<Alarm?> GetAlarmByIdAsync(int id);
        Task<IEnumerable<AlarmEvent>> GetAllAlarmEventsAsync();
        Task<IEnumerable<Group>> GetAllGroupsAsync(int? enterpriseId);
        Task<Group?> GetGroupByIdAsync(int id);
        Task<IEnumerable<UserInfo>> GetManagedUsersAsync(string token);
        Task<UserInfo?> CreateManagedUserAsync(CreateManagedUserDto dto, string token);
        Task<bool> DeleteManagedUserAsync(int userId, string token);
        Task<IEnumerable<Enterprise>> GetUserEnterprisesAsync(int userId, string token);
        Task<bool> AddUserToEnterpriseAsync(int userId, int enterpriseId, string token);
        Task<bool> RemoveUserFromEnterpriseAsync(int userId, int enterpriseId, string token);
    }
}

