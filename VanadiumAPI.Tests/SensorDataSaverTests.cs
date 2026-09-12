using Data.Mongo;
using Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Models;
using Shared.Models.Mqtt;
using VanadiumAPI.Services.AlarmRegistry;
using Xunit;

namespace VanadiumAPI.Tests
{
    /// <summary>
    /// Exercises the telemetry ingestion pipeline (PushSensorData -> channel -> ProcessSensorDataMessage)
    /// through the real BackgroundService, since that pipeline is private and channel-driven.
    /// </summary>
    public class SensorDataSaverTests : IAsyncLifetime
    {
        private readonly Mock<IPanelInfoRepository> _panelRepo = new();
        private readonly Mock<IPanelReadingRepository> _readingRepo = new();
        private readonly Mock<IAlarmRegistryService> _alarmRegistry = new();
        private VanadiumAPI.SensorDataSaver.SensorDataSaver _saver = null!;
        private readonly CancellationTokenSource _cts = new();

        public async Task InitializeAsync()
        {
            _panelRepo.Setup(r => r.GetAllPanels(null)).ReturnsAsync(new List<Panel>());

            var services = new ServiceCollection();
            services.AddSingleton(_panelRepo.Object);
            services.AddSingleton(_readingRepo.Object);
            var provider = services.BuildServiceProvider();

            _saver = new VanadiumAPI.SensorDataSaver.SensorDataSaver(
                new Mock<ILogger<VanadiumAPI.SensorDataSaver.SensorDataSaver>>().Object,
                provider,
                _alarmRegistry.Object);
            await _saver.StartAsync(_cts.Token);
        }

        public async Task DisposeAsync()
        {
            _cts.Cancel();
            try { await _saver.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
        {
            var start = DateTime.UtcNow;
            while (!condition())
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException("Condition was not met in time");
                await Task.Delay(10);
            }
        }

        private static SensorDataMessageModel MakeMessage(string gatewayId, int sensorId, float value, string type = "flow") => new()
        {
            GatewayId = gatewayId,
            Telemetry = new DeviceTelemetryPayload
            {
                DeviceId = gatewayId,
                Timestamp = DateTime.UtcNow,
                Readings = new List<SensorReading> { new() { SensorId = sensorId, Type = type, Value = value } }
            }
        };

        [Fact]
        public async Task PushSensorData_KnownFlowPanel_UpdatesLastReadingAndPersistsImmediately()
        {
            var panel = new Panel { Id = 1, GatewayId = "GW1", Index = "0", Type = PanelType.Flow, Alarms = new List<Alarm>() };
            _panelRepo.Setup(r => r.GetPanelByGatewayAndIndexAsync("GW1", "0")).ReturnsAsync(panel);

            _saver.PushSensorData(MakeMessage("GW1", 0, 12.5f));

            await WaitUntilAsync(() => _saver.GetLastPanelReading(1) != null);
            var last = _saver.GetLastPanelReading(1);
            Assert.NotNull(last);
            Assert.Equal(12.5f, last!.Value);
            Assert.True(last.Active);

            _alarmRegistry.Verify(a => a.ProcessReadingForAlarmsAsync(panel, It.IsAny<PanelReading>(), It.IsAny<CancellationToken>()), Times.Once);

            // Flow readings are saved on every tick, not just the periodic 1-minute flush.
            await WaitUntilAsync(() => _readingRepo.Invocations.Any(i => i.Method.Name == nameof(IPanelReadingRepository.AddAsync)));
            _readingRepo.Verify(r => r.AddAsync(It.Is<IEnumerable<PanelReading>>(l => l.Any(x => x.PanelId == 1 && x.Value == 12.5f))), Times.Once);
        }

        [Fact]
        public async Task PushSensorData_NonFlowPanel_UpdatesLastReadingButDoesNotPersistImmediately()
        {
            var panel = new Panel { Id = 2, GatewayId = "GW2", Index = "0", Type = PanelType.Temperature, Alarms = new List<Alarm>() };
            _panelRepo.Setup(r => r.GetPanelByGatewayAndIndexAsync("GW2", "0")).ReturnsAsync(panel);

            _saver.PushSensorData(MakeMessage("GW2", 0, 21.3f, type: "temperature"));

            await WaitUntilAsync(() => _saver.GetLastPanelReading(2) != null);
            Assert.Equal(21.3f, _saver.GetLastPanelReading(2)!.Value);

            // Give the async pipeline a moment to settle, then confirm no immediate persistence happened.
            await Task.Delay(100);
            _readingRepo.Verify(r => r.AddAsync(It.IsAny<IEnumerable<PanelReading>>()), Times.Never);
        }

        [Fact]
        public async Task PushSensorData_UnknownPanel_DoesNotUpdateAnyReadingOrProcessAlarms()
        {
            _panelRepo.Setup(r => r.GetPanelByGatewayAndIndexAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((Panel?)null);

            _saver.PushSensorData(MakeMessage("UNKNOWN", 0, 1f));

            // Nothing to poll for a positive result, so just give the background consumer a chance to run.
            await Task.Delay(100);
            _alarmRegistry.Verify(a => a.ProcessReadingForAlarmsAsync(It.IsAny<Panel>(), It.IsAny<PanelReading>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task RemovePanel_ClearsLastReading()
        {
            var panel = new Panel { Id = 3, GatewayId = "GW3", Index = "0", Type = PanelType.Flow, Alarms = new List<Alarm>() };
            _panelRepo.Setup(r => r.GetPanelByGatewayAndIndexAsync("GW3", "0")).ReturnsAsync(panel);
            _saver.PushSensorData(MakeMessage("GW3", 0, 5f));
            await WaitUntilAsync(() => _saver.GetLastPanelReading(3) != null);

            _saver.RemovePanel(3, "GW3", "0");

            Assert.Null(_saver.GetLastPanelReading(3));
        }
    }
}
