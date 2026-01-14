using System.Collections.Concurrent;
using API.Hubs;
using Microsoft.AspNetCore.SignalR;
using Shared.Models;

namespace API.Services
{
    public class PanelBroadcastService : IPanelBroadcastService
    {
        private readonly IHubContext<PanelReadingsHub> _hubContext;
        private readonly ILogger<PanelBroadcastService> _logger;
        // ConnectionId -> Set of GatewayIds
        private readonly ConcurrentDictionary<string, HashSet<string>> _connectionGatewayIds = new();

        // ConnectionId -> UserId (for tracking which user owns the connection)
        private readonly ConcurrentDictionary<string, int> _connectionUsers = new();

        // PanelId -> Set of ConnectionIds (reverse lookup for efficient broadcasting)
        private readonly ConcurrentDictionary<int, HashSet<string>> _panelConnections = new();

        public PanelBroadcastService(
            IHubContext<PanelReadingsHub> hubContext,
            ILogger<PanelBroadcastService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public void SubscribeToPanel(string connectionId, int userId, int panelId, string gatewayId)
        {
            // Track the user for this connection
            _connectionUsers.TryAdd(connectionId, userId);

            // Add panel to connection's subscriptions
            _connectionGatewayIds.AddOrUpdate(
                connectionId,
                new HashSet<string> { gatewayId },
                (_, panels) =>
                {
                    panels.Add(gatewayId);
                    return panels;
                });

            // Add connection to panel's subscribers
            _panelConnections.AddOrUpdate(
                panelId,
                new HashSet<string> { connectionId },
                (_, connections) =>
                {
                    connections.Add(connectionId);
                    return connections;
                });

            _logger.LogInformation(
                "User {UserId} (Connection {ConnectionId}) subscribed to panel {PanelId}",
                userId, connectionId, panelId);
        }

        public void UnsubscribeFromPanel(string connectionId, int panelId, string gatewayId)
        {
            // Remove panel from connection's subscriptions
            if (_connectionGatewayIds.TryGetValue(connectionId, out var gatewayIds))
            {
                gatewayIds.Remove(gatewayId);
            }

            // Remove connection from panel's subscribers
            if (_panelConnections.TryGetValue(panelId, out var connections))
            {
                connections.Remove(connectionId);

                // Clean up if no connections left
                if (connections.Count == 0)
                {
                    _panelConnections.TryRemove(panelId, out _);
                }
            }

            var userId = _connectionUsers.TryGetValue(connectionId, out var uid) ? uid : -1;
            _logger.LogInformation(
                "User {UserId} (Connection {ConnectionId}) unsubscribed from panel {PanelId}",
                userId, connectionId, panelId);
        }

        public void SetUserPanels(string connectionId, int userId, IEnumerable<(int panelId, string gatewayId)> panelGatewayIds)
        {
            RemoveConnection(connectionId);

            // Subscribe to new panels
            foreach (var panelGatewayId in panelGatewayIds)
            {
                SubscribeToPanel(connectionId, userId, panelGatewayId.panelId, panelGatewayId.gatewayId);
            }

            _logger.LogInformation(
                "User {UserId} (Connection {ConnectionId}) subscribed to {Count} panels",
                userId, connectionId, panelGatewayIds.Count());
        }

        public async Task BroadcastSensorData(SensorDataMessageModel sensorData)
        {
            // Get all connections subscribed to this panel
            if (_connectionGatewayIds.TryGetValue(sensorData.GatewayId, out var connections) && connections.Any())
            {

                await _hubContext.Clients
                    .Clients(connections.ToList())
                    .SendAsync("SensorDataReceived", sensorData);
            }
        }

        public async Task BroadcastPanelChange(PanelChangeMessage panelChange)
        {
            if (_panelConnections.TryGetValue(panelChange.Panel.Id, out var connections) && connections.Any())
            {
                _logger.LogDebug(
                    "Broadcasting panel change for panel {PanelId} to {Count} connections",
                    panelChange.Panel.Id, connections.Count);

                await _hubContext.Clients
                    .Clients(connections.ToList())
                    .SendAsync("PanelChangeReceived", panelChange);
            }
        }

        public void RemoveConnection(string connectionId)
        {
            // Get all panels this connection was subscribed to
            if (_connectionGatewayIds.TryRemove(connectionId, out var panels))
            {
                _panelConnections.Where(kvp => kvp.Value.Contains(connectionId))
                    .ToList()
                    .ForEach(kvp =>
                    {
                        kvp.Value.Remove(connectionId);
                        if (kvp.Value.Count == 0)
                        {
                            _panelConnections.TryRemove(kvp.Key, out _);
                        }
                    });
            }

            var userId = _connectionUsers.TryGetValue(connectionId, out var uid) ? uid : -1;
            _connectionUsers.TryRemove(connectionId, out _);

            _logger.LogInformation(
                "Removed all subscriptions for User {UserId} (Connection {ConnectionId})",
                userId, connectionId);
        }

        // Diagnostic method to see current state
        public Dictionary<string, object> GetSubscriptionStats()
        {
            return new Dictionary<string, object>
            {
                ["TotalConnections"] = _connectionGatewayIds.Count,
                ["TotalPanelsWithSubscribers"] = _panelConnections.Count,
                ["ConnectionDetails"] = _connectionGatewayIds.Select(kvp => new
                {
                    ConnectionId = kvp.Key,
                    UserId = _connectionUsers.TryGetValue(kvp.Key, out var uid) ? uid : -1,
                    PanelCount = kvp.Value.Count,
                    Panels = kvp.Value.ToList()
                }).ToList()
            };
        }
    }
}