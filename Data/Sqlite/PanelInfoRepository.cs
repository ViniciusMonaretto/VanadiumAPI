using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Collections.Generic;
using Models;
using System.Linq;
using System;

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
        public async Task<IEnumerable<Panel>> GetAllPanels()
        {
            return await _context.Panels.ToListAsync();
        }
        public async Task<Panel?> GetPanelById(int id)
        {
            return await _context.Panels.FindAsync(id);
        }
        public async Task<IEnumerable<Group>> GetAllGroups()
        {
            return await _context.Groups.ToListAsync();
        }
        public async Task<Group?> GetGroupById(int id)
        {
            return await _context.Groups.FindAsync(id);
        }
        public async Task<IEnumerable<Alarm>> GetAllAlarms()
        {
            return await _context.Alarms.ToListAsync();
        }
        public async Task<Alarm?> GetAlarmById(int id)
        {
            return await _context.Alarms.FindAsync(id);
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