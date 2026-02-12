using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Shared.Models;
using VanadiumAPI.Hubs;

namespace VanadiumAPI.Services
{
    public class PanelBroadcastService : IPanelBroadcastService
    {
        private readonly IHubContext<PanelReadingsHub> _hubContext;
        private readonly ILogger<PanelBroadcastService> _logger;
        private readonly ConcurrentDictionary<string, HashSet<string>> _subscribedConnectionsToGatewayIds = new();
        private readonly ConcurrentDictionary<string, int> _connectionUsers = new();
        private readonly ConcurrentDictionary<int, HashSet<string>> _panelConnections = new();

        public PanelBroadcastService(IHubContext<PanelReadingsHub> hubContext, ILogger<PanelBroadcastService> logger)
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

        public void SetUserPanels(string connectionId, int userId, IEnumerable<(int panelId, string gatewayId)> panelGatewayIds)
        {
            RemoveConnection(connectionId);
            foreach (var (panelId, gatewayId) in panelGatewayIds)
                SubscribeToPanel(connectionId, userId, panelId, gatewayId);
        }

        public async Task BroadcastSensorData(SensorDataMessageModel sensorData)
        {
            if (_subscribedConnectionsToGatewayIds.TryGetValue(sensorData.GatewayId, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("SensorDataReceived", sensorData);
        }

        public async Task BroadcastPanelChange(PanelChangeMessage panelChange)
        {
            if (_panelConnections.TryGetValue(panelChange.Panel.Id, out var connections) && connections.Any())
                await _hubContext.Clients.Clients(connections.ToList()).SendAsync("PanelChangeReceived", panelChange);
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
            _connectionUsers.TryRemove(connectionId, out _);
        }
    }
}
