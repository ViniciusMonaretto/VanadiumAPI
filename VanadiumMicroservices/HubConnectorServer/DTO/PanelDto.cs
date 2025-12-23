using Shared.Models;

namespace HubConnectorServer.DTO
{
    public class PanelDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string GatewayId { get; set; }
        public int GroupId { get; set; }
        public string Index { get; set; }
        public string Color { get; set; }
        public float Gain { get; set; }
        public float Offset { get; set; }
        public List<AlarmDto> Alarms { get; set; }

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
            Alarms = panel.Alarms.Select(a => new AlarmDto(a)).ToList();
        }

        public PanelDto()
        {
        }
    }
}