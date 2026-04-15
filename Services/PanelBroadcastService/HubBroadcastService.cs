using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Shared.Models;
using VanadiumAPI.DTO;
using VanadiumAPI.Hubs;

namespace VanadiumAPI.Services
{
    public class HubBroadcastService : IHubBroadcastService
    {
        private readonly IHubContext<PanelReadingsHub> _hubContext;
        private readonly ILogger<HubBroadcastService> _logger;
        private readonly ConcurrentDictionary<string, HashSet<string>> _subscribedConnectionsToGatewayIds = new();
        private readonly ConcurrentDictionary<string, int> _connectionUsers = new();
        private readonly ConcurrentDictionary<string, int> _connectionEnterprises = new();
        private readonly ConcurrentDictionary<int, HashSet<string>> _panelConnections = new();
        private readonly ConcurrentDictionary<int, HashSet<string>> _enterpriseConnections = new();

        public HubBroadcastService(IHubContext<PanelReadingsHub> hubContext, ILogger<HubBroadcastService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public void SubscribeToPanel(string connectionId, int userId, int panelId, string gatewayId)
        {
            _connectionUsers.TryAdd(connectionId, userId);

            _subscribedConnectionsToGatewayIds.AddOrUpdate(
                gatewayId,
                new HashSet<string> { connectionId },
                (_, connections) => { connections.Add(connectionId); return connections; });

            _panelConnections.AddOrUpdate(
                panelId,
                new HashSet<string> { connectionId },
                (_, connections) => { connections.Add(connectionId); return connections; });
        }

        public void UnsubscribeFromPanel(string connectionId, int panelId, string gatewayId)
        {
            if (_subscribedConnectionsToGatewayIds.TryGetValue(gatewayId, out var connectionIds))
            {
                connectionIds.Remove(connectionId);
                if (connectionIds.Count == 0)
                    _subscribedConnectionsToGatewayIds.TryRemove(gatewayId, out _);
            }

            if (_panelConnections.TryGetValue(panelId, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                    _panelConnections.TryRemove(panelId, out _);
            }
        }

        public void SetUserPanels(string connectionId, int userId, int enterpriseId, IEnumerable<(int panelId, string gatewayId)> panelGatewayIds)
        {
            RemoveConnection(connectionId);
            _connectionEnterprises[connectionId] = enterpriseId;
            _enterpriseConnections.AddOrUpdate(
                enterpriseId,
                new HashSet<string> { connectionId },
                (_, set) => { set.Add(connectionId); return set; });
            foreach (var (panelId, gatewayId) in panelGatewayIds)
                SubscribeToPanel(connectionId, userId, panelId, gatewayId);
        }

        public int? GetConnectionEnterpriseId(string connectionId)
        {
            return _connectionEnterprises.TryGetValue(connectionId, out var enterpriseId) ? enterpriseId : null;
        }

        public async Task BroadcastSensorData(SensorDataMessageModel sensorData)
        {
            if (_subscribedConnectionsToGatewayIds.TryGetValue(sensorData.GatewayId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("SensorDataReceived", sensorData);
        }

        public async Task BroadcastPanelChange(PanelChangeAction action, PanelDto panelDto)
        {
            if (_panelConnections.TryGetValue(panelDto.Id, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("PanelChangeReceived", new PanelChangeMessageDto { Action = action, Panel = panelDto });
        }

        public async Task BroadcastGatewaySystemInfo(int enterpriseId, SystemMessageModel systemData)
        {
            if (_enterpriseConnections.TryGetValue(enterpriseId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("GatewaySystemInfoReceived", systemData);
        }

        public void RemoveConnection(string connectionId)
        {
            foreach (var kvp in _subscribedConnectionsToGatewayIds.Where(k => k.Value.Contains(connectionId)).ToList())
            {
                kvp.Value.Remove(connectionId);
                if (kvp.Value.Count == 0)
                    _subscribedConnectionsToGatewayIds.TryRemove(kvp.Key, out _);
            }
            foreach (var kvp in _panelConnections.Where(k => k.Value.Contains(connectionId)).ToList())
            {
                kvp.Value.Remove(connectionId);
                if (kvp.Value.Count == 0)
                    _panelConnections.TryRemove(kvp.Key, out _);
            }
            if (_connectionEnterprises.TryRemove(connectionId, out var enterpriseId) &&
                _enterpriseConnections.TryGetValue(enterpriseId, out var enterpriseConns))
            {
                enterpriseConns.Remove(connectionId);
                if (enterpriseConns.Count == 0)
                    _enterpriseConnections.TryRemove(enterpriseId, out _);
            }
            _connectionUsers.TryRemove(connectionId, out _);
        }

        public async Task BroadcastGatewayAdded(int enterpriseId, string gatewayId)
        {
            if (_enterpriseConnections.TryGetValue(enterpriseId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("GatewayAdded", gatewayId);
        }

        public async Task BroadcastGatewayRemoved(int enterpriseId, string gatewayId)
        {
            if (_enterpriseConnections.TryGetValue(enterpriseId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("GatewayRemoved", gatewayId);
        }

        public void RemoveGateway(string gatewayId, IEnumerable<int> panelIds)
        {
            _subscribedConnectionsToGatewayIds.TryRemove(gatewayId, out _);
            foreach (var panelId in panelIds)
                _panelConnections.TryRemove(panelId, out _);
        }

        public async Task BroadcastPanelAdded(int enterpriseId, PanelDto panelDto)
        {
            if (_enterpriseConnections.TryGetValue(enterpriseId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("PanelAdded", panelDto);
        }

        public async Task BroadcastPanelRemoved(int enterpriseId, int panelId)
        {
            if (_enterpriseConnections.TryGetValue(enterpriseId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("PanelRemoved", panelId);
        }

        public void AddPanel(int enterpriseId, int panelId, string gatewayId)
        {
            if (!_enterpriseConnections.TryGetValue(enterpriseId, out var connections)) return;
            foreach (var connectionId in connections.ToList())
            {
                var userId = _connectionUsers.TryGetValue(connectionId, out var uid) ? uid : 0;
                SubscribeToPanel(connectionId, userId, panelId, gatewayId);
            }
        }

        public void RemovePanel(int panelId, string gatewayId)
        {
            if (!_panelConnections.TryGetValue(panelId, out var connections)) return;
            foreach (var connectionId in connections.ToList())
                UnsubscribeFromPanel(connectionId, panelId, gatewayId);
        }

        public async Task BroadcastGroupCreated(int enterpriseId, GroupDto groupDto)
        {
            if (_enterpriseConnections.TryGetValue(enterpriseId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("GroupCreated", groupDto);
        }

        public async Task BroadcastGroupUpdated(int enterpriseId, GroupDto groupDto)
        {
            if (_enterpriseConnections.TryGetValue(enterpriseId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("GroupUpdated", groupDto);
        }

        public async Task BroadcastGroupRemoved(int enterpriseId, int groupId)
        {
            if (_enterpriseConnections.TryGetValue(enterpriseId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("GroupRemoved", groupId);
        }

        public async Task BroadcastAlarmEvent(int enterpriseId, AlarmEventDto alarmEvent)
        {
            if (_enterpriseConnections.TryGetValue(enterpriseId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("AlarmEventReceived", alarmEvent);
        }
    }
}
