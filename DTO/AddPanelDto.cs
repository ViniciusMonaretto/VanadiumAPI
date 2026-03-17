using Shared.Models;

namespace VanadiumAPI.DTO
{
    public class AddPanelDto
    {
        public string Name { get; set; } = string.Empty;
        public string GatewayId { get; set; } = string.Empty;
        public int GroupId { get; set; }
        public string Index { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public float Gain { get; set; } = 1;
        public float Offset { get; set; } = 0;
        public int Multiplier { get; set; } = 1;
        public PanelType Type { get; set; }
    }
}
