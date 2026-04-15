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
        /// <summary>Latest high-threshold alarm for this panel (value above threshold triggers), if any.</summary>
        public float? MaxAlarm { get; set; }
        /// <summary>Latest low-threshold alarm for this panel (value below threshold triggers), if any.</summary>
        public float? MinAlarm { get; set; }
        /// <summary>Time of the last in-memory sample for this panel (MQTT pipeline).</summary>
        public DateTime? LastReadingTime { get; set; }
        /// <summary>Value of the last in-memory sample for this panel (MQTT pipeline).</summary>
        public float? Value { get; set; }
        /// <summary>Active flag from the last in-memory sample (MQTT <c>active</c>); null if none received yet.</summary>
        public bool? Active { get; set; }
        public int DisplayedType { get; set; }
        /// <summary>Flow aggregates from Mongo; null for non-flow panels.</summary>
        public FlowConsumptionDto? FlowConsumption { get; set; }

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
            DisplayedType = panel.DisplayedType;
            Alarms = panel.Alarms?.Select(a => new AlarmDto(a)).ToList() ?? new List<AlarmDto>();
            MaxAlarm = panel.Alarms?.Where(a => a.IsGreaterThan).OrderByDescending(a => a.Id).FirstOrDefault()?.Threshold;
            MinAlarm = panel.Alarms?.Where(a => !a.IsGreaterThan).OrderByDescending(a => a.Id).FirstOrDefault()?.Threshold;
        }

        public PanelDto() { }
    }
}
