using Shared.Models;

namespace VanadiumAPI.DTO
{
    public class PanelDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string GatewayId { get; set; } = string.Empty;
        public int GroupId { get; set; }
        public string Index { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public float Gain { get; set; }
        public float Offset { get; set; }
        public int Multiplier { get; set; }
        public PanelType Type { get; set; }
        public List<AlarmDto> Alarms { get; set; } = new List<AlarmDto>();

        public PanelDto(Panel panel)
        {
            Id = panel.Id;
            Name = panel.Name;
            GatewayId = panel.GatewayId;
            GroupId = panel.GroupId;
            Index = panel.Index;
            Color = panel.Color;
            Gain = panel.Gain;
            Offset = panel.Offset;
            Multiplier = panel.Multiplier;
            Type = panel.Type;
            Alarms = panel.Alarms?.Select(a => new AlarmDto(a)).ToList() ?? new List<AlarmDto>();
        }

        public PanelDto() { }
    }
}
