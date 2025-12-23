using Microsoft.AspNetCore.SignalR;
using Shared.Models;
using API.Services;
using HubConnectorServer.DTO;

namespace API.Hubs
{
    public class PanelReadingsHub : Hub
    {
        private readonly ISensorInfoService _sensorInfoService;
        private readonly ILogger<PanelReadingsHub> _logger;

        public PanelReadingsHub(ISensorInfoService sensorInfoService, ILogger<PanelReadingsHub> logger)
        {
            _sensorInfoService = sensorInfoService;
            _logger = logger;
        }

                // Connection lifecycle
        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
            
            // Automatically send all groups info to newly connected client
            try
            {
                var groups = await GetAllGroups();
                await Clients.Caller.SendAsync("updateGroupsInfo", groups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all groups on connection");
                // Don't fail the connection if groups fetch fails
            }
            
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }


        // Panels
        public async Task<IEnumerable<PanelDto>> GetAllPanels()
        {
            try
            {
                var panels = await _sensorInfoService.GetAllPanelsAsync();
                return panels.Select(p => new PanelDto(p));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all panels");
                throw;
            }
        }

        public async Task<PanelDto?> GetPanelById(int id)
        {
            try
            {
                var panel = await _sensorInfoService.GetPanelByIdAsync(id);
                return panel != null ? new PanelDto(panel) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching panel {PanelId}", id);
                throw;
            }
        }

        // Alarms
        public async Task<IEnumerable<AlarmDto>> GetAllAlarms()
        {
            try
            {
                var alarms = await _sensorInfoService.GetAllAlarmsAsync();
                return alarms.Select(a => new AlarmDto(a));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all alarms");
                throw;
            }
        }

        public async Task<AlarmDto?> GetAlarmById(int id)
        {
            try
            {
                var alarm = await _sensorInfoService.GetAlarmByIdAsync(id);
                return alarm != null ? new AlarmDto(alarm) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching alarm {AlarmId}", id);
                throw;
            }
        }

        // Alarm Events
        public async Task<IEnumerable<AlarmEventDto>> GetAllAlarmEvents()
        {
            try
            {
                var alarmEvents = await _sensorInfoService.GetAllAlarmEventsAsync();
                if (alarmEvents == null)
                {
                    _logger.LogWarning("GetAllAlarmEventsAsync returned null");
                    return Enumerable.Empty<AlarmEventDto>();
                }
                
                var result = alarmEvents
                    .Where(ae => ae != null)
                    .Select(ae => new AlarmEventDto(ae))
                    .ToList();
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all alarm events: {Message}", ex.Message);
                throw;
            }
        }

        // Groups
        public async Task<Dictionary<string, GroupDto>> GetAllGroups()
        {
            try
            {
                var groups = await _sensorInfoService.GetAllGroupsAsync();
                return groups.ToDictionary(g => g.Id.ToString(), g => new GroupDto(g));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all groups");
                throw;
            }
        }

        public async Task<GroupDto?> GetGroupById(int id)
        {
            try
            {
                var group = await _sensorInfoService.GetGroupByIdAsync(id);
                return group != null ? new GroupDto(group) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching group {GroupId}", id);
                throw;
            }
        }
    }
}