using Microsoft.AspNetCore.SignalR;

public class ClientHub : Hub
{
    public async Task BroadcastMessage(string user, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}