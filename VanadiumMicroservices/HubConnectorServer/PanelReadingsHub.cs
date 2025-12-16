using Microsoft.AspNetCore.SignalR;
using Shared.Models;

namespace API.Hubs
{
    public class PanelReadingsHub : Hub
    {
        // Send reading to all connected clients
        public async Task SendPanelReading(PanelReading reading)
        {
            await Clients.All.SendAsync("ReceivePanelReading", reading);
        }

        // Send reading to specific panel subscribers
        public async Task SendPanelReadingToGroup(int panelId, PanelReading reading)
        {
            await Clients.Group($"Panel_{panelId}").SendAsync("ReceivePanelReading", reading);
        }

        // Subscribe to specific panel updates
        public async Task SubscribeToPanel(int panelId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Panel_{panelId}");
        }

        // Unsubscribe from panel updates
        public async Task UnsubscribeFromPanel(int panelId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Panel_{panelId}");
        }

        // Connection lifecycle
        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            await base.OnDisconnectedAsync(exception);
        }
    }
}