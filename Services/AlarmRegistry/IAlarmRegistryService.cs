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

        /// <summary>Records an IoCloud Predict fault start as an event on the panel's anomaly alarm
        /// (created on first use). Idempotent per <paramref name="zoneId"/>. True if a new event was recorded.</summary>
        Task<bool> ProcessPredictionStartedAsync(Panel panel, string zoneId, DateTime start, AlarmSeverity severity, float triggeredValue, CancellationToken cancellationToken = default);

        /// <summary>Events of the given panels (only those in <paramref name="enterpriseId"/>), oldest first,
        /// optionally limited to a time range and alarm kind.</summary>
        Task<IReadOnlyList<AlarmEvent>> GetAlarmEventsForPanelsAsync(int enterpriseId, IReadOnlyCollection<int> panelIds, DateTime? startDate, DateTime? endDate, AlarmKind? kind, int maxEvents = 5000, CancellationToken cancellationToken = default);
    }
}
