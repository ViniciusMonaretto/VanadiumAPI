using Data.Sqlite;
using Microsoft.AspNetCore.SignalR;

public class ClientHub : Hub
{
    private readonly IPanelInfoRepository _panelInfoRepository;
    public ClientHub(IPanelInfoRepository panelInfoRepository)
    {
        _panelInfoRepository = panelInfoRepository;
    }
    public async Task BroadcastMessage(string user, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}