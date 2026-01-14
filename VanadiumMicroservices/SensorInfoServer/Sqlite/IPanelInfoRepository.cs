using System.Threading.Tasks;
using System.Collections.Generic;
using Shared.Models;
using System.Linq.Expressions;

namespace Data.Sqlite
{
    public interface IPanelInfoRepository
    {
        void Add<T>(T entity) where T : class;
        void Delete<T>(T entity) where T : class;
        Task<bool> SaveAll();
        Task<IEnumerable<Panel>> GetAllPanels(Expression<Func<Panel, bool>>? filter = null);
        Task<Panel?> GetPanelById(int id);
        Task<IEnumerable<Group>> GetAllGroups();
        Task<Group?> GetGroupById(int id);
        Task<IEnumerable<Group>> GetEnterpriseGroups(int enterpriseId);
        Task<IEnumerable<Alarm>> GetAllAlarms();
        Task<Alarm?> GetAlarmById(int id);
        Task<IEnumerable<AlarmEvent>> GetAllAlarmEvents();
        Task<AlarmEvent?> GetAlarmEventById(int id);
    }
}