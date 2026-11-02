using System.Collections.Concurrent;
using Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Models;
using VanadiumAPI.DTO;
using VanadiumAPI.Services;

namespace VanadiumAPI.Services.AlarmRegistry
{
    public sealed class AlarmRegistryService : IAlarmRegistryService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubBroadcastService _hubBroadcast;
        private readonly ILogger<AlarmRegistryService> _logger;

        private readonly object _sync = new();
        private Dictionary<int, List<AlarmRule>> _rulesByPanel = new();
        private Dictionary<int, AlarmRule> _rulesById = new();

        private readonly ConcurrentDictionary<int, bool> _breachActiveByAlarmId = new();
        private readonly SemaphoreSlim _predictionLock = new(1, 1);

        public AlarmRegistryService(
            IServiceScopeFactory scopeFactory,
            IHubBroadcastService hubBroadcast,
            ILogger<AlarmRegistryService> logger)
        {
            _scopeFactory = scopeFactory;
            _hubBroadcast = hubBroadcast;
            _logger = logger;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();

            var alarmRows = await (
                from a in db.Alarms.AsNoTracking()
                join p in db.Panels.AsNoTracking() on a.PanelId equals p.Id
                join g in db.Groups.AsNoTracking() on p.GroupId equals g.Id
                select new { a, g.EnterpriseId }
            ).ToListAsync(cancellationToken);
            var eventCount = await db.AlarmEvents.AsNoTracking().CountAsync(cancellationToken);

            var byPanel = new Dictionary<int, List<AlarmRule>>();
            var byId = new Dictionary<int, AlarmRule>();

            foreach (var row in alarmRows)
            {
                var a = row.a;
                var rule = new AlarmRule(a.Id, a.PanelId, a.Threshold, a.IsGreaterThan, row.EnterpriseId, a.Severity, a.Kind);
                byId[a.Id] = rule;
                if (!byPanel.TryGetValue(a.PanelId, out var list))
                {
                    list = new List<AlarmRule>();
                    byPanel[a.PanelId] = list;
                }
                list.Add(rule);
            }

            lock (_sync)
            {
                _rulesByPanel = byPanel;
                _rulesById = byId;
            }

            _breachActiveByAlarmId.Clear();
            _logger.LogInformation(
                "Alarm registry loaded {AlarmCount} alarm definitions; {EventCount} events remain in the database only",
                alarmRows.Count,
                eventCount);
        }

        public async Task<IReadOnlyList<Alarm>> GetAllAlarmsAsync(CancellationToken cancellationToken = default)
        {
            List<AlarmRule> rulesSnapshot;
            lock (_sync)
            {
                rulesSnapshot = _rulesById.Values.OrderBy(r => r.Id).ToList();
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();
            var allEvents = await db.AlarmEvents.AsNoTracking()
                .OrderBy(e => e.EventTime)
                .ToListAsync(cancellationToken);
            var byAlarmId = allEvents.GroupBy(e => e.AlarmId).ToDictionary(g => g.Key, g => g.ToList());

            var list = new List<Alarm>(rulesSnapshot.Count);
            foreach (var rule in rulesSnapshot)
            {
                byAlarmId.TryGetValue(rule.Id, out var evs);
                list.Add(ToAlarm(rule, evs ?? new List<AlarmEvent>()));
            }
            return list;
        }

        public async Task<Alarm?> GetAlarmByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            AlarmRule rule;
            bool found;
            lock (_sync)
            {
                found = _rulesById.TryGetValue(id, out rule);
            }
            if (!found) return null;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();
            var events = await db.AlarmEvents.AsNoTracking()
                .Where(e => e.AlarmId == id)
                .OrderBy(e => e.EventTime)
                .ToListAsync(cancellationToken);
            return ToAlarm(rule, events);
        }

        public async Task<IReadOnlyList<AlarmEvent>> GetAllAlarmEventsAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();
            return await db.AlarmEvents.AsNoTracking()
                .Include(e => e.Alarm)
                .OrderByDescending(e => e.EventTime)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Alarm>> GetAlarmsForEnterpriseAsync(int enterpriseId, int maxAlarms = 100, CancellationToken cancellationToken = default)
        {
            if (maxAlarms < 1) maxAlarms = 1;
            if (maxAlarms > 500) maxAlarms = 500;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();

            var alarmRows = await (
                from a in db.Alarms.AsNoTracking()
                join p in db.Panels.AsNoTracking() on a.PanelId equals p.Id
                join g in db.Groups.AsNoTracking() on p.GroupId equals g.Id
                where g.EnterpriseId == enterpriseId
                orderby a.Id
                select new { a, g.EnterpriseId }
            ).Take(maxAlarms).ToListAsync(cancellationToken);

            if (alarmRows.Count == 0)
                return Array.Empty<Alarm>();

            var ids = alarmRows.Select(x => x.a.Id).ToList();
            var events = await db.AlarmEvents.AsNoTracking()
                .Where(e => ids.Contains(e.AlarmId))
                .OrderBy(e => e.EventTime)
                .ToListAsync(cancellationToken);
            var byAlarmId = events.GroupBy(e => e.AlarmId).ToDictionary(g => g.Key, g => g.ToList());

            return alarmRows
                .Select(row => ToAlarm(
                    new AlarmRule(row.a.Id, row.a.PanelId, row.a.Threshold, row.a.IsGreaterThan, row.EnterpriseId, row.a.Severity, row.a.Kind),
                    byAlarmId.TryGetValue(row.a.Id, out var evs) ? evs : new List<AlarmEvent>()))
                .ToList();
        }

        public async Task<IReadOnlyList<AlarmEvent>> GetAlarmEventsForEnterpriseAsync(int enterpriseId, int maxEvents = 100, CancellationToken cancellationToken = default)
        {
            if (maxEvents < 1) maxEvents = 1;
            if (maxEvents > 500) maxEvents = 500;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();

            var ids = await (
                from ev in db.AlarmEvents.AsNoTracking()
                join a in db.Alarms.AsNoTracking() on ev.AlarmId equals a.Id
                join p in db.Panels.AsNoTracking() on a.PanelId equals p.Id
                join g in db.Groups.AsNoTracking() on p.GroupId equals g.Id
                where g.EnterpriseId == enterpriseId
                orderby ev.EventTime descending
                select ev.Id
            ).Take(maxEvents).ToListAsync(cancellationToken);

            if (ids.Count == 0)
                return Array.Empty<AlarmEvent>();

            var events = await db.AlarmEvents.AsNoTracking()
                .Include(e => e.Alarm)
                .Where(e => ids.Contains(e.Id))
                .ToListAsync(cancellationToken);

            return events.OrderByDescending(e => e.EventTime).ToList();
        }

        public void NotifyAlarmCreated(Alarm alarm)
        {
            if (alarm == null || alarm.Id == 0) return;
            var enterpriseId = ResolveEnterpriseIdForPanel(alarm.PanelId);
            if (enterpriseId <= 0)
                _logger.LogWarning("Could not resolve EnterpriseId for new alarm {AlarmId} on panel {PanelId}", alarm.Id, alarm.PanelId);
            var rule = new AlarmRule(alarm.Id, alarm.PanelId, alarm.Threshold, alarm.IsGreaterThan, enterpriseId, alarm.Severity, alarm.Kind);
            lock (_sync)
            {
                _rulesById[alarm.Id] = rule;
                if (!_rulesByPanel.TryGetValue(alarm.PanelId, out var list))
                {
                    list = new List<AlarmRule>();
                    _rulesByPanel[alarm.PanelId] = list;
                }
                list.Add(rule);
            }
        }

        public void NotifyAlarmUpdated(Alarm alarm)
        {
            if (alarm == null || alarm.Id == 0) return;

            AlarmRule? oldRuleNullable;
            lock (_sync)
            {
                oldRuleNullable = _rulesById.TryGetValue(alarm.Id, out var r) ? r : null;
            }

            if (oldRuleNullable == null)
            {
                NotifyAlarmCreated(alarm);
                return;
            }

            var oldRule = oldRuleNullable.Value;
            var enterpriseId = oldRule.PanelId == alarm.PanelId
                ? oldRule.EnterpriseId
                : ResolveEnterpriseIdForPanel(alarm.PanelId);
            if (enterpriseId <= 0)
                enterpriseId = oldRule.EnterpriseId;

            var newRule = new AlarmRule(alarm.Id, alarm.PanelId, alarm.Threshold, alarm.IsGreaterThan, enterpriseId, alarm.Severity, alarm.Kind);

            lock (_sync)
            {
                if (!_rulesById.ContainsKey(alarm.Id))
                    return;

                _rulesById[alarm.Id] = newRule;

                if (oldRule.PanelId != alarm.PanelId)
                {
                    if (_rulesByPanel.TryGetValue(oldRule.PanelId, out var oldList))
                    {
                        oldList.RemoveAll(r => r.Id == alarm.Id);
                        if (oldList.Count == 0)
                            _rulesByPanel.Remove(oldRule.PanelId);
                    }
                    if (!_rulesByPanel.TryGetValue(alarm.PanelId, out var newList))
                    {
                        newList = new List<AlarmRule>();
                        _rulesByPanel[alarm.PanelId] = newList;
                    }
                    if (!newList.Any(r => r.Id == alarm.Id))
                        newList.Add(newRule);
                }
                else if (_rulesByPanel.TryGetValue(alarm.PanelId, out var list))
                {
                    var idx = list.FindIndex(r => r.Id == alarm.Id);
                    if (idx >= 0)
                        list[idx] = newRule;
                    else
                        list.Add(newRule);
                }
            }

            _breachActiveByAlarmId.TryRemove(alarm.Id, out _);
        }

        public void NotifyAlarmDeleted(int alarmId)
        {
            lock (_sync)
            {
                if (!_rulesById.TryGetValue(alarmId, out var rule))
                    return;
                _rulesById.Remove(alarmId);
                if (_rulesByPanel.TryGetValue(rule.PanelId, out var list))
                {
                    list.RemoveAll(r => r.Id == alarmId);
                    if (list.Count == 0)
                        _rulesByPanel.Remove(rule.PanelId);
                }
            }
            _breachActiveByAlarmId.TryRemove(alarmId, out _);
        }

        public async Task ProcessReadingForAlarmsAsync(Panel panel, PanelReading reading, CancellationToken cancellationToken = default)
        {
            if (!reading.Active) return;

            var value = reading.Value;
            List<AlarmRule> rules;
            lock (_sync)
            {
                rules = _rulesByPanel.TryGetValue(panel.Id, out var r) ? r.ToList() : new List<AlarmRule>();
            }

            foreach (var rule in rules)
            {
                if (rule.Kind != AlarmKind.Threshold)
                    continue; // anomaly alarms fire from predictions, never from readings
                var triggered = Evaluate(rule, value);
                var wasActive = _breachActiveByAlarmId.GetOrAdd(rule.Id, false);

                if (triggered)
                {
                    if (!wasActive)
                    {
                        if (await TryPersistAlarmEventAsync(rule, reading.ReadingTime, value, panel.Name ?? string.Empty, cancellationToken))
                            _breachActiveByAlarmId[rule.Id] = true;
                    }
                }
                else if (wasActive)
                {
                    _breachActiveByAlarmId[rule.Id] = false;
                }
            }
        }

        private static Alarm ToAlarm(AlarmRule rule, List<AlarmEvent> events) =>
            new Alarm
            {
                Id = rule.Id,
                PanelId = rule.PanelId,
                Threshold = rule.Threshold,
                IsGreaterThan = rule.IsGreaterThan,
                Severity = rule.Severity,
                Kind = rule.Kind,
                AlarmEvents = events
            };

        private int ResolveEnterpriseIdForPanel(int panelId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();
            return (
                from p in db.Panels.AsNoTracking()
                join g in db.Groups.AsNoTracking() on p.GroupId equals g.Id
                where p.Id == panelId
                select g.EnterpriseId
            ).FirstOrDefault();
        }

        /// <summary>
        /// IoCloud Predict reported a fault starting on <paramref name="panel"/>. Records one
        /// <see cref="AlarmEvent"/> on the panel's anomaly alarm (created on first use) and broadcasts it,
        /// unless <paramref name="zoneId"/> was already recorded — predictions may be redelivered and the
        /// retained state message repeats the open fault on every reconnect.
        /// </summary>
        /// <returns>True when a new event was recorded.</returns>
        public async Task<bool> ProcessPredictionStartedAsync(
            Panel panel,
            string zoneId,
            DateTime start,
            AlarmSeverity severity,
            float triggeredValue,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(zoneId))
                return false;

            // Serialized so two predictions for a new panel cannot both create its anomaly alarm.
            await _predictionLock.WaitAsync(cancellationToken);
            try
            {
                var rule = await GetOrCreateAnomalyRuleAsync(panel, severity, cancellationToken);
                if (rule == null)
                    return false;

                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();
                    var seen = await db.AlarmEvents.AsNoTracking()
                        .AnyAsync(e => e.AlarmId == rule.Value.Id && e.ExternalId == zoneId, cancellationToken);
                    if (seen)
                        return false;
                }

                return await TryPersistAlarmEventAsync(rule.Value, start, triggeredValue, panel.Name ?? string.Empty,
                    cancellationToken, externalId: zoneId);
            }
            finally
            {
                _predictionLock.Release();
            }
        }

        public async Task<IReadOnlyList<AlarmEvent>> GetAlarmEventsForPanelsAsync(
            int enterpriseId,
            IReadOnlyCollection<int> panelIds,
            DateTime? startDate,
            DateTime? endDate,
            AlarmKind? kind,
            int maxEvents = 5000,
            CancellationToken cancellationToken = default)
        {
            if (panelIds.Count == 0)
                return Array.Empty<AlarmEvent>();
            if (maxEvents < 1) maxEvents = 1;
            if (maxEvents > 5000) maxEvents = 5000;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();
            var query = db.AlarmEvents.AsNoTracking()
                .Include(e => e.Alarm)
                .Where(e => panelIds.Contains(e.Alarm.PanelId) && e.Alarm.Panel.Group.EnterpriseId == enterpriseId);
            if (startDate.HasValue)
                query = query.Where(e => e.EventTime >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(e => e.EventTime <= endDate.Value);
            if (kind.HasValue)
                query = query.Where(e => e.Alarm.Kind == kind.Value);
            return await query
                .OrderBy(e => e.EventTime)
                .Take(maxEvents)
                .ToListAsync(cancellationToken);
        }

        /// <summary>The panel's anomaly alarm, created (severity from the first prediction) if it has none yet.</summary>
        private async Task<AlarmRule?> GetOrCreateAnomalyRuleAsync(Panel panel, AlarmSeverity severity, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_rulesByPanel.TryGetValue(panel.Id, out var list))
                {
                    foreach (var r in list)
                        if (r.Kind == AlarmKind.Anomaly)
                            return r;
                }
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();
                var alarm = new Alarm
                {
                    PanelId = panel.Id,
                    Kind = AlarmKind.Anomaly,
                    Threshold = 0,
                    IsGreaterThan = false,
                    Severity = severity
                };
                db.Alarms.Add(alarm);
                await db.SaveChangesAsync(cancellationToken);
                NotifyAlarmCreated(alarm);
                _logger.LogInformation("Created anomaly alarm {AlarmId} for panel {PanelId}", alarm.Id, panel.Id);

                lock (_sync)
                {
                    return _rulesById.TryGetValue(alarm.Id, out var created) ? created : null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create anomaly alarm for panel {PanelId}", panel.Id);
                return null;
            }
        }

        private async Task<bool> TryPersistAlarmEventAsync(
            AlarmRule rule,
            DateTime eventTime,
            float triggeredValue,
            string panelName,
            CancellationToken cancellationToken,
            string? externalId = null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();
                var ev = new AlarmEvent
                {
                    AlarmId = rule.Id,
                    PanelId = rule.PanelId,
                    EventTime = eventTime,
                    TriggeredValue = triggeredValue,
                    PanelName = panelName ?? string.Empty,
                    ExternalId = externalId
                };
                db.AlarmEvents.Add(ev);
                await db.SaveChangesAsync(cancellationToken);

                if (rule.EnterpriseId > 0)
                    await _hubBroadcast.BroadcastAlarmEvent(rule.EnterpriseId, new AlarmEventDto(ev, alarmSeverity: rule.Severity, alarmKind: rule.Kind));

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist alarm event for alarm {AlarmId}", rule.Id);
                return false;
            }
        }

        private static bool Evaluate(AlarmRule rule, float value) =>
            rule.IsGreaterThan ? value > rule.Threshold : value < rule.Threshold;

        private readonly record struct AlarmRule(int Id, int PanelId, float Threshold, bool IsGreaterThan, int EnterpriseId, AlarmSeverity Severity, AlarmKind Kind);
    }
}
