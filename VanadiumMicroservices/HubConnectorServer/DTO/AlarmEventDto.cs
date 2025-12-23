using Shared.Models;

namespace HubConnectorServer.DTO
{
    public class AlarmEventDto
    {
        public int Id { get; set; }
        public int AlarmId { get; set; }
        public DateTime EventTime { get; set; }
        
        // Parameterless constructor for serialization
        public AlarmEventDto()
        {
        }
        
        public AlarmEventDto(AlarmEvent alarmEvent)
        {
            if (alarmEvent == null)
                throw new ArgumentNullException(nameof(alarmEvent));
                
            Id = alarmEvent.Id;
            AlarmId = alarmEvent.AlarmId;
            EventTime = alarmEvent.EventTime;
        }
    }
    
}