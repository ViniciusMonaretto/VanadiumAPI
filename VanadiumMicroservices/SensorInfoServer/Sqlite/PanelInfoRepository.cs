using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Collections.Generic;
using Shared.Models;
using System.Linq;
using System;
using System.Linq.Expressions;

namespace Data.Sqlite
{
    public class PanelInfoRepository : IPanelInfoRepository	
    {
        private readonly SqliteDataContext _context;
        public PanelInfoRepository(SqliteDataContext context)
        {
            _context = context;
        }
        public void Add<T>(T entity) where T : class
        {
            _context.Add(entity);
        }
        public void Delete<T>(T entity) where T : class
        {
            _context.Remove(entity);
        }

        public async Task<bool> SaveAll()
        {
            return await _context.SaveChangesAsync() > 0;
        }
        public async Task<IEnumerable<Panel>> GetAllPanels(Expression<Func<Panel, bool>> filter = null)
        {
            IQueryable<Panel> query = _context.Panels.Include(p => p.Alarms);

            if (filter != null)
                query = query.Where(filter);

            return await query.ToListAsync();
        }
        public async Task<Panel?> GetPanelById(int id)
        {
            return await _context.Panels
                .Include(p => p.Alarms)
                .FirstOrDefaultAsync(p => p.Id == id);
        }
        public async Task<IEnumerable<Group>> GetAllGroups()
        {
            return await _context.Groups
                .Include(g => g.Panels)
                .ThenInclude(p => p.Alarms)
                .ThenInclude(a => a.AlarmEvents)
                .ToListAsync();
        }
        public async Task<Group?> GetGroupById(int id)
        {
            return await _context.Groups
                .Include(g => g.Panels)
                .ThenInclude(p => p.Alarms)
                .FirstOrDefaultAsync(g => g.Id == id);
        }
        public async Task<IEnumerable<Alarm>> GetAllAlarms()
        {
            return await _context.Alarms
                .Include(a => a.AlarmEvents)
                .ToListAsync();
        }
        public async Task<Alarm?> GetAlarmById(int id)
        {
            return await _context.Alarms
                .Include(a => a.AlarmEvents)
                .FirstOrDefaultAsync(a => a.Id == id);
        }
        public async Task<IEnumerable<AlarmEvent>> GetAllAlarmEvents()
        {
            return await _context.AlarmEvents.ToListAsync();
        }
        public async Task<AlarmEvent?> GetAlarmEventById(int id)
        {
            return await _context.AlarmEvents.FindAsync(id);
        }
    }
}