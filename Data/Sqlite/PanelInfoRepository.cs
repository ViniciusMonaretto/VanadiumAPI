using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Shared.Models;

namespace Data.Sqlite
{
    public class PanelInfoRepository : IPanelInfoRepository
    {
        private readonly SqliteDataContext _context;

        public PanelInfoRepository(SqliteDataContext context)
        {
            _context = context;
        }

        public void Add<T>(T entity) where T : class => _context.Add(entity);
        public void Delete<T>(T entity) where T : class => _context.Remove(entity);

        public async Task<bool> SaveAll() => await _context.SaveChangesAsync() > 0;

        public async Task<IEnumerable<Panel>> GetAllPanels(Expression<Func<Panel, bool>>? filter = null)
        {
            var query = _context.Panels.Include(p => p.Alarms).AsQueryable();
            if (filter != null)
                query = query.Where(filter);
            return await query.ToListAsync();
        }

        public async Task<Panel?> GetPanelById(int id) =>
            await _context.Panels.Include(p => p.Alarms).FirstOrDefaultAsync(p => p.Id == id);

        public async Task<Panel?> CreatePanelAsync(Panel panel)
        {
            _context.Add(panel);
            if (await _context.SaveChangesAsync() <= 0) return null;
            return panel;
        }

        public async Task<bool> UpdatePanelAsync(Panel panel)
        {
            if (_context.Entry(panel).State == EntityState.Detached)
                _context.Panels.Update(panel);
            // Zero writes is valid when no scalar property changed (e.g. only alarm patches in the same request).
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeletePanelAsync(Panel panel)
        {
            _context.Remove(panel);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<Panel?> GetPanelByGatewayAndIndexAsync(string gatewayId, string index) =>
            await _context.Panels.FirstOrDefaultAsync(p => p.GatewayId == gatewayId && p.Index == index);

        public async Task<IEnumerable<Group>> GetAllGroups() =>
            await _context.Groups
                .Include(g => g.Panels)
                .ThenInclude(p => p.Alarms)
                .ThenInclude(a => a.AlarmEvents)
                .ToListAsync();

        public async Task<Group?> GetGroupById(int id) =>
            await _context.Groups
                .Include(g => g.Panels)
                .ThenInclude(p => p.Alarms)
                .FirstOrDefaultAsync(g => g.Id == id);

        public async Task<IEnumerable<Group>> GetEnterpriseGroups(int enterpriseId) =>
            await _context.Groups
                .Include(g => g.Panels)
                .ThenInclude(p => p.Alarms)
                .ThenInclude(a => a.AlarmEvents)
                .Where(g => g.EnterpriseId == enterpriseId)
                .ToListAsync();

        public async Task<IEnumerable<Gateway>> GetGatewaysByEnterpriseId(int enterpriseId) =>
            await _context.Gateways
                .Include(g => g.Panels)
                .Where(g => g.EnterpriseId == enterpriseId)
                .ToListAsync();

        public async Task<IEnumerable<Gateway>> GetAllGatewaysAsync() =>
            await _context.Gateways.ToListAsync();

        public async Task<Gateway?> GetGatewayByGatewayIdAsync(string gatewayId) =>
            await _context.Gateways.FirstOrDefaultAsync(g => g.GatewayId == gatewayId);

        public async Task<IEnumerable<Alarm>> GetAllAlarms() =>
            await _context.Alarms.Include(a => a.AlarmEvents).ToListAsync();

        public async Task<Alarm?> GetAlarmById(int id) =>
            await _context.Alarms.Include(a => a.AlarmEvents).FirstOrDefaultAsync(a => a.Id == id);

        public async Task<IEnumerable<AlarmEvent>> GetAllAlarmEvents() =>
            await _context.AlarmEvents.ToListAsync();

        public async Task<AlarmEvent?> GetAlarmEventById(int id) =>
            await _context.AlarmEvents.FindAsync(id);
    }
}
