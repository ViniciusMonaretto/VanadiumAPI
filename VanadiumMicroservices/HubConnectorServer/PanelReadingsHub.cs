using Microsoft.AspNetCore.SignalR;
using Shared.Models;
using API.Services;

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
                var groups = (await _sensorInfoService.GetAllGroupsAsync()).ToDictionary(g => g.Id, g => g);
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


        // Get all panels from SensorInfoServer
        public async Task GetAllPanels()
        {
            try
            {
                var panels = await _sensorInfoService.GetAllPanelsAsync();
                await Clients.Caller.SendAsync("ReceiveAllPanels", panels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all panels");
                await Clients.Caller.SendAsync("Error", "Failed to fetch panels");
            }
        }

        // Get panel by ID from SensorInfoServer
        public async Task GetPanelById(int id)
        {
            try
            {
                var panel = await _sensorInfoService.GetPanelByIdAsync(id);
                if (panel == null)
                {
                    await Clients.Caller.SendAsync("Error", $"Panel with id {id} not found");
                }
                else
                {
                    await Clients.Caller.SendAsync("ReceivePanel", panel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching panel {PanelId}", id);
                await Clients.Caller.SendAsync("Error", $"Failed to fetch panel {id}");
            }
        }

        // Get all alarms from SensorInfoServer
        public async Task GetAllAlarms()
        {
            try
            {
                var alarms = await _sensorInfoService.GetAllAlarmsAsync();
                await Clients.Caller.SendAsync("ReceiveAllAlarms", alarms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all alarms");
                await Clients.Caller.SendAsync("Error", "Failed to fetch alarms");
            }
        }

        // Get alarm by ID from SensorInfoServer
        public async Task GetAlarmById(int id)
        {
            try
            {
                var alarm = await _sensorInfoService.GetAlarmByIdAsync(id);
                if (alarm == null)
                {
                    await Clients.Caller.SendAsync("Error", $"Alarm with id {id} not found");
                }
                else
                {
                    await Clients.Caller.SendAsync("ReceiveAlarm", alarm);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching alarm {AlarmId}", id);
                await Clients.Caller.SendAsync("Error", $"Failed to fetch alarm {id}");
            }
        }

        // Get all alarm events from SensorInfoServer
        public async Task GetAllAlarmEvents()
        {
            try
            {
                var alarmEvents = await _sensorInfoService.GetAllAlarmEventsAsync();
                await Clients.Caller.SendAsync("ReceiveAllAlarmEvents", alarmEvents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all alarm events");
                await Clients.Caller.SendAsync("Error", "Failed to fetch alarm events");
            }
        }

        // Get all groups from SensorInfoServer
        public async Task GetAllGroups()
        {
            try
            {
                var groups = await _sensorInfoService.GetAllGroupsAsync();
                await Clients.Caller.SendAsync("ReceiveAllGroups", groups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all groups");
                await Clients.Caller.SendAsync("Error", "Failed to fetch groups");
            }
        }

        // Get group by ID from SensorInfoServer
        public async Task GetGroupById(int id)
        {
            try
            {
                var group = await _sensorInfoService.GetGroupByIdAsync(id);
                if (group == null)
                {
                    await Clients.Caller.SendAsync("Error", $"Group with id {id} not found");
                }
                else
                {
                    await Clients.Caller.SendAsync("ReceiveGroup", group);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching group {GroupId}", id);
                await Clients.Caller.SendAsync("Error", $"Failed to fetch group {id}");
            }
        }
    }
}