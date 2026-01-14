using Shared.Models;

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
    }
}

