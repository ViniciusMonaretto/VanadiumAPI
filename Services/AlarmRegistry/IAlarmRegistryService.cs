using Shared.Models;

namespace VanadiumAPI.Services.AlarmRegistry
{
    public interface IAlarmRegistryService
    {
        Task InitializeAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Alarm>> GetAllAlarmsAsync(CancellationToken cancellationToken = default);

        Task<Alarm?> GetAlarmByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AlarmEvent>> GetAllAlarmEventsAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Alarm>> GetAlarmsForEnterpriseAsync(int enterpriseId, int maxAlarms = 100, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AlarmEvent>> GetAlarmEventsForEnterpriseAsync(int enterpriseId, int maxEvents = 100, CancellationToken cancellationToken = default);

        void NotifyAlarmCreated(Alarm alarm);

        void NotifyAlarmUpdated(Alarm alarm);

        void NotifyAlarmDeleted(int alarmId);

        Task ProcessReadingForAlarmsAsync(Panel panel, PanelReading reading, CancellationToken cancellationToken = default);
    }
}
