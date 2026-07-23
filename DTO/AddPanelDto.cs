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
        public int SamplingMs { get; set; } = 1000;
        public bool Enabled { get; set; } = true;
        public PanelType Type { get; set; }
    }
}
