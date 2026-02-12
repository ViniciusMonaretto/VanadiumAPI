using Shared.Models;

namespace VanadiumAPI.DTO
{
    public class AlarmDto
    {
        public int Id { get; set; }
        public int PanelId { get; set; }
        public float Threshold { get; set; }
        public bool IsGreaterThan { get; set; }
        public List<AlarmEventDto> AlarmEvents { get; set; } = new List<AlarmEventDto>();

        public AlarmDto(Alarm alarm)
        {
            Id = alarm.Id;
            PanelId = alarm.PanelId;
            Threshold = alarm.Threshold;
            IsGreaterThan = alarm.IsGreaterThan;
            AlarmEvents = alarm.AlarmEvents?.Select(ae => new AlarmEventDto(ae)).ToList() ?? new List<AlarmEventDto>();
        }

        public AlarmDto() { }
    }
}
