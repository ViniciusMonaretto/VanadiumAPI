using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using API.Hubs;
using Shared.Models;

namespace API.Services
{
    public class HubEventService : IHubEventService
    {
        private readonly IHubContext<PanelReadingsHub> _hubContext;
        private readonly ILogger<HubEventService> _logger;
        private readonly HashSet<int> _subscribedPanels;
        private bool _subscribeToAll;

        public HubEventService(
            IHubContext<PanelReadingsHub> hubContext,
            ILogger<HubEventService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
            _subscribedPanels = new HashSet<int>();
            _subscribeToAll = true; // Default to all panels
        }

        public async Task SendSensorDataReceived(SensorDataMessageModel sensorData, CancellationToken cancellationToken)
        {
            try
            {
                // Filter based on subscriptions if not subscribed to all
                if (!_subscribeToAll && !ShouldSendSensorData(sensorData))
                {
                    return;
                }

                await _hubContext.Clients.All.SendAsync("SensorDataReceived", sensorData, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending sensor data received event");
                throw;
            }
        }

        public async Task SendPanelChangeReceived(PanelChangeMessage panelChange, CancellationToken cancellationToken)
        {
            try
            {
                // Filter based on subscriptions if not subscribed to all
                if (!_subscribeToAll && !ShouldSendPanelChange(panelChange))
                {
                    return;
                }

                await _hubContext.Clients.All.SendAsync("PanelChangeReceived", panelChange, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending panel change received event");
                throw;
            }
        }

        public void SubscribeToPanel(int panelId)
        {
            _subscribeToAll = false;
            _subscribedPanels.Add(panelId);
            _logger.LogInformation("Subscribed to panel {PanelId}", panelId);
        }

        public void UnsubscribeFromPanel(int panelId)
        {
            _subscribedPanels.Remove(panelId);
            _logger.LogInformation("Unsubscribed from panel {PanelId}", panelId);
        }

        public void SubscribeToAllPanels()
        {
            _subscribeToAll = true;
            _subscribedPanels.Clear();
            _logger.LogInformation("Subscribed to all panels");
        }

        public bool IsSubscribedToPanel(int panelId)
        {
            return _subscribeToAll || _subscribedPanels.Contains(panelId);
        }

        public bool IsSubscribedToAll()
        {
            return _subscribeToAll;
        }

        private bool ShouldSendSensorData(SensorDataMessageModel sensorData)
        {
            // Extract panel ID from sensor data if available
            // This depends on your data structure - you may need to adjust this
            // For now, we'll send if subscribed to all or if we can't determine panel ID
            // You'll need to implement the logic to extract panel ID from sensorData
            // For example, if sensorData has a PanelId or GatewayId that maps to panels
            return true; // Placeholder - implement your filtering logic
        }

        private bool ShouldSendPanelChange(PanelChangeMessage panelChange)
        {
            if (panelChange?.Panel == null)
            {
                return false;
            }

            return _subscribedPanels.Contains(panelChange.Panel.Id);
        }
    }
}
