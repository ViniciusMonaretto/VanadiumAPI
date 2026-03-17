using Shared.Models;

namespace VanadiumAPI.DTO
{
    /// <summary>Serializable payload for SignalR "PanelChangeReceived" (avoids Panel entity cycle).</summary>
    public class PanelChangeMessageDto
    {
        public PanelChangeAction Action { get; set; }
        public PanelDto Panel { get; set; } = null!;
    }
}
