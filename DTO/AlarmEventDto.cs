using Shared.Models;

namespace VanadiumAPI.DTO
{
    public class AlarmEventDto
    {
        public int Id { get; set; }
        public int AlarmId { get; set; }
        public DateTime EventTime { get; set; }

        public AlarmEventDto(AlarmEvent alarmEvent)
        {
            if (alarmEvent == null)
                throw new ArgumentNullException(nameof(alarmEvent));
            Id = alarmEvent.Id;
            AlarmId = alarmEvent.AlarmId;
            EventTime = alarmEvent.EventTime;
        }

        public AlarmEventDto() { }
    }
}
