using Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Models;
using VanadiumAPI.DTO;
using VanadiumAPI.Services;
using VanadiumAPI.Services.AlarmRegistry;

namespace VanadiumAPI.Tests
{
    /// <summary>
    /// IoCloud Predict anomaly alarms in <see cref="AlarmRegistryService"/>, against a real in-memory
    /// SQLite database so the schema (Kind, ExternalId, unique index) and queries are exercised too.
    /// </summary>
    public class AnomalyAlarmTests : IDisposable
    {
        private static readonly DateTime T0 = new(2026, 11, 4, 17, 15, 44, DateTimeKind.Utc);

        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private readonly ServiceProvider _provider;
        private readonly Mock<IHubBroadcastService> _hub = new();
        private readonly int _enterpriseId;
        private readonly Panel _panel;

        public AnomalyAlarmTests()
        {
            _connection.Open();
            var services = new ServiceCollection();
            services.AddDbContext<SqliteDataContext>(o => o.UseSqlite(_connection));
            _provider = services.BuildServiceProvider();

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();
            _enterpriseId = db.Enterprises.First().Id;
            _panel = AddPanel(db, _enterpriseId, "PREDGW01");
        }

        public void Dispose()
        {
            _provider.Dispose();
            _connection.Dispose();
        }

        private static Panel AddPanel(SqliteDataContext db, int enterpriseId, string gatewayId)
        {
            var group = new Group { Name = $"Group {gatewayId}", EnterpriseId = enterpriseId };
            db.Groups.Add(group);
            db.Gateways.Add(new Gateway { GatewayId = gatewayId, EnterpriseId = enterpriseId });
            db.SaveChanges();
            var panel = new Panel
            {
                Name = $"Geladeira {gatewayId}", GatewayId = gatewayId, GroupId = group.Id, Index = "0",
                Color = "#2563eb", Gain = 1, Type = PanelType.Temperature
            };
            db.Panels.Add(panel);
            db.SaveChanges();
            return panel;
        }

        private async Task<AlarmRegistryService> CreateRegistryAsync()
        {
            var registry = new AlarmRegistryService(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                _hub.Object,
                new Mock<ILogger<AlarmRegistryService>>().Object);
            await registry.InitializeAsync();
            return registry;
        }

        private T WithDb<T>(Func<SqliteDataContext, T> query)
        {
            using var scope = _provider.CreateScope();
            return query(scope.ServiceProvider.GetRequiredService<SqliteDataContext>());
        }

        // ---------- ProcessPredictionStartedAsync ----------

        [Fact]
        public async Task FirstPrediction_CreatesAnomalyAlarmAndEvent_AndBroadcastsIt()
        {
            var registry = await CreateRegistryAsync();

            var created = await registry.ProcessPredictionStartedAsync(_panel, "zone-1", T0, AlarmSeverity.Warning, 3.4f);

            Assert.True(created);
            var alarm = Assert.Single(WithDb(db => db.Alarms.Where(a => a.PanelId == _panel.Id).ToList()));
            Assert.Equal(AlarmKind.Anomaly, alarm.Kind);
            Assert.Equal(AlarmSeverity.Warning, alarm.Severity);
            var ev = Assert.Single(WithDb(db => db.AlarmEvents.ToList()));
            Assert.Equal(alarm.Id, ev.AlarmId);
            Assert.Equal("zone-1", ev.ExternalId);
            Assert.Equal(T0, DateTime.SpecifyKind(ev.EventTime, DateTimeKind.Utc));
            Assert.Equal(3.4f, ev.TriggeredValue);
            _hub.Verify(h => h.BroadcastAlarmEvent(_enterpriseId,
                It.Is<AlarmEventDto>(d => d.Kind == AlarmKind.Anomaly && d.PanelId == _panel.Id)), Times.Once);
        }

        [Fact]
        public async Task SameZoneTwice_RecordsOnce()
        {
            var registry = await CreateRegistryAsync();

            Assert.True(await registry.ProcessPredictionStartedAsync(_panel, "zone-1", T0, AlarmSeverity.Warning, 3f));
            Assert.False(await registry.ProcessPredictionStartedAsync(_panel, "zone-1", T0, AlarmSeverity.Warning, 3f));

            Assert.Single(WithDb(db => db.AlarmEvents.ToList()));
            _hub.Verify(h => h.BroadcastAlarmEvent(It.IsAny<int>(), It.IsAny<AlarmEventDto>()), Times.Once);
        }

        [Fact]
        public async Task NewZone_ReusesThePanelsAnomalyAlarm()
        {
            var registry = await CreateRegistryAsync();

            await registry.ProcessPredictionStartedAsync(_panel, "zone-1", T0, AlarmSeverity.Warning, 3f);
            await registry.ProcessPredictionStartedAsync(_panel, "zone-2", T0.AddDays(1), AlarmSeverity.Critical, 4f);

            Assert.Single(WithDb(db => db.Alarms.Where(a => a.PanelId == _panel.Id).ToList()));
            Assert.Equal(2, WithDb(db => db.AlarmEvents.Count()));
        }

        [Fact]
        public async Task AfterRestart_AlarmIsReloaded_DuplicatesStillDetected()
        {
            var first = await CreateRegistryAsync();
            await first.ProcessPredictionStartedAsync(_panel, "zone-1", T0, AlarmSeverity.Warning, 3f);

            var restarted = await CreateRegistryAsync();
            Assert.False(await restarted.ProcessPredictionStartedAsync(_panel, "zone-1", T0, AlarmSeverity.Warning, 3f));
            Assert.True(await restarted.ProcessPredictionStartedAsync(_panel, "zone-2", T0.AddDays(1), AlarmSeverity.Warning, 3f));

            Assert.Single(WithDb(db => db.Alarms.Where(a => a.PanelId == _panel.Id).ToList()));
        }

        [Fact]
        public async Task EmptyZoneId_IsIgnored()
        {
            var registry = await CreateRegistryAsync();
            Assert.False(await registry.ProcessPredictionStartedAsync(_panel, " ", T0, AlarmSeverity.Warning, 3f));
            Assert.Empty(WithDb(db => db.Alarms.ToList().Where(a => a.PanelId == _panel.Id)));
        }

        [Fact]
        public void UniqueIndex_RejectsDuplicateExternalIdPerAlarm()
        {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqliteDataContext>();
            var alarm = new Alarm { PanelId = _panel.Id, Kind = AlarmKind.Anomaly };
            db.Alarms.Add(alarm);
            db.SaveChanges();
            db.AlarmEvents.Add(new AlarmEvent { AlarmId = alarm.Id, PanelId = _panel.Id, EventTime = T0, ExternalId = "z" });
            db.SaveChanges();

            db.AlarmEvents.Add(new AlarmEvent { AlarmId = alarm.Id, PanelId = _panel.Id, EventTime = T0, ExternalId = "z" });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        // ---------- threshold alarms are unaffected ----------

        [Fact]
        public async Task Readings_NeverFireAnomalyAlarms_ButStillFireThresholdAlarms()
        {
            var registry = await CreateRegistryAsync();
            await registry.ProcessPredictionStartedAsync(_panel, "zone-1", T0, AlarmSeverity.Warning, 3f);

            var high = new Alarm { PanelId = _panel.Id, Threshold = 8f, IsGreaterThan = true, Kind = AlarmKind.Threshold };
            WithDb(db => { db.Alarms.Add(high); return db.SaveChanges(); });
            registry.NotifyAlarmCreated(high);

            await registry.ProcessReadingForAlarmsAsync(_panel,
                new PanelReading { PanelId = _panel.Id, ReadingTime = T0.AddHours(1), Value = 9f, Active = true });

            var events = WithDb(db => db.AlarmEvents.ToList());
            Assert.Equal(2, events.Count);
            Assert.Single(events, e => e.AlarmId == high.Id && e.ExternalId == null);
        }

        // ---------- GetAlarmEventsForPanelsAsync ----------

        [Fact]
        public async Task GetAlarmEventsForPanels_FiltersByKindRangeAndEnterprise()
        {
            var registry = await CreateRegistryAsync();
            await registry.ProcessPredictionStartedAsync(_panel, "zone-1", T0, AlarmSeverity.Warning, 3f);
            await registry.ProcessPredictionStartedAsync(_panel, "zone-2", T0.AddDays(2), AlarmSeverity.Warning, 3f);

            var high = new Alarm { PanelId = _panel.Id, Threshold = 8f, IsGreaterThan = true };
            WithDb(db => { db.Alarms.Add(high); return db.SaveChanges(); });
            registry.NotifyAlarmCreated(high);
            await registry.ProcessReadingForAlarmsAsync(_panel,
                new PanelReading { PanelId = _panel.Id, ReadingTime = T0.AddHours(1), Value = 9f, Active = true });

            var anomalies = await registry.GetAlarmEventsForPanelsAsync(_enterpriseId, new[] { _panel.Id }, null, null, AlarmKind.Anomaly);
            Assert.Equal(new[] { "zone-1", "zone-2" }, anomalies.Select(e => e.ExternalId));
            Assert.All(anomalies, e => Assert.Equal(AlarmKind.Anomaly, e.Alarm.Kind));

            var all = await registry.GetAlarmEventsForPanelsAsync(_enterpriseId, new[] { _panel.Id }, null, null, null);
            Assert.Equal(3, all.Count);

            var inRange = await registry.GetAlarmEventsForPanelsAsync(
                _enterpriseId, new[] { _panel.Id }, T0.AddDays(1), T0.AddDays(3), AlarmKind.Anomaly);
            Assert.Equal("zone-2", Assert.Single(inRange).ExternalId);

            var otherEnterprise = await registry.GetAlarmEventsForPanelsAsync(_enterpriseId + 999, new[] { _panel.Id }, null, null, null);
            Assert.Empty(otherEnterprise);
        }

        // ---------- schema upgrade of existing databases ----------

        [Fact]
        public void ExistingDatabase_WithoutNewColumns_IsUpgradedOnStartup()
        {
            // Simulate an app.db from before this change: drop what the new code adds.
            WithDb(db =>
            {
                db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_AlarmEvents_AlarmId_ExternalId");
                db.Database.ExecuteSqlRaw("ALTER TABLE AlarmEvents DROP COLUMN ExternalId");
                db.Database.ExecuteSqlRaw("ALTER TABLE Alarms DROP COLUMN Kind");
                return 0;
            });
            Assert.False(HasColumn("Alarms", "Kind"));

            // Constructing the context runs the Ensure* upgrades.
            WithDb(db => db.Alarms.Count());

            Assert.True(HasColumn("Alarms", "Kind"));
            Assert.True(HasColumn("AlarmEvents", "ExternalId"));
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='index' AND name='IX_AlarmEvents_AlarmId_ExternalId'";
            Assert.NotNull(cmd.ExecuteScalar());
        }

        private bool HasColumn(string table, string column)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name='{column}'";
            return cmd.ExecuteScalar() != null;
        }

        // ---------- DTOs ----------

        [Fact]
        public void PanelDto_MinMaxIgnoreTheAnomalyAlarm()
        {
            var panel = new Panel
            {
                Id = 1, Name = "P", GatewayId = "GW", Index = "0", Color = "#000",
                Alarms = new List<Alarm>
                {
                    new() { Id = 1, Threshold = 8f, IsGreaterThan = true, Severity = AlarmSeverity.Critical },
                    new() { Id = 2, Threshold = 2f, IsGreaterThan = false },
                    new() { Id = 3, Kind = AlarmKind.Anomaly, Threshold = 0f, IsGreaterThan = false },
                }
            };

            var dto = new PanelDto(panel);

            Assert.Equal(8f, dto.MaxAlarm);
            Assert.Equal(2f, dto.MinAlarm);
            Assert.Equal((int)AlarmSeverity.Critical, dto.Severity);
            Assert.Contains(dto.Alarms, a => a.Kind == AlarmKind.Anomaly);
        }

        [Fact]
        public async Task EventsReadBackFromTheDatabase_SerializeAsUtc()
        {
            var registry = await CreateRegistryAsync();
            await registry.ProcessPredictionStartedAsync(_panel, "zone-1", T0, AlarmSeverity.Warning, 3f);

            var stored = Assert.Single(await registry.GetAlarmEventsForPanelsAsync(_enterpriseId, new[] { _panel.Id }, null, null, null));
            var json = System.Text.Json.JsonSerializer.Serialize(new AlarmEventDto(stored));

            Assert.Contains("\"Timestamp\":\"2026-11-04T17:15:44Z\"", json);
        }

        [Fact]
        public void AlarmEventDto_TakesKindFromAlarm()
        {
            var ev = new AlarmEvent { Id = 1, AlarmId = 3, PanelId = 1, Alarm = new Alarm { Kind = AlarmKind.Anomaly } };
            Assert.Equal(AlarmKind.Anomaly, new AlarmEventDto(ev).Kind);
            Assert.Equal(AlarmKind.Threshold, new AlarmEventDto(new AlarmEvent()).Kind);
        }
    }
}
