using Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Models;
using VanadiumAPI.SensorDataSaver;
using VanadiumAPI.Services.AlarmRegistry;
using VanadiumAPI.Services.Prediction;

namespace VanadiumAPI.Tests
{
    /// <summary>Routing of iocloud/{id}/predict and /predict/state messages to the alarm registry.</summary>
    public class PredictionServiceTests
    {
        private static readonly DateTime T0 = new(2026, 11, 4, 17, 15, 44, DateTimeKind.Utc);

        private readonly Mock<IPanelInfoRepository> _repo = new();
        private readonly Mock<IAlarmRegistryService> _registry = new();
        private readonly Mock<ISensorDataSaver> _saver = new();
        private readonly Panel _panel = new() { Id = 7, Name = "Geladeira", GatewayId = "DEV1", Index = "0" };

        private PredictionService CreateService()
        {
            var services = new ServiceCollection();
            services.AddSingleton(_repo.Object);
            var provider = services.BuildServiceProvider();
            _repo.Setup(r => r.GetPanelByGatewayAndIndexAsync("DEV1", "0")).ReturnsAsync(_panel);
            _saver.Setup(s => s.GetLastPanelReading(7)).Returns(new PanelReading { Value = 4.2f });
            return new PredictionService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                _registry.Object,
                _saver.Object,
                new Mock<ILogger<PredictionService>>().Object);
        }

        private static string Prediction(string type = "anomaly_started", int version = 1, int sensorId = 0,
                                         string severity = "warning") =>
            $$"""
            {"version": {{version}}, "device_id": "DEV1", "sensor_id": {{sensorId}}, "zone_id": "z1",
             "type": "{{type}}", "kind": "fault", "severity": "{{severity}}",
             "start": "2026-11-04T17:15:44Z", "end": null, "timestamp": "2026-11-04T17:15:44Z"}
            """;

        private void VerifyStarted(Times times, AlarmSeverity severity = AlarmSeverity.Warning) =>
            _registry.Verify(r => r.ProcessPredictionStartedAsync(_panel, "z1", T0, severity, 4.2f, It.IsAny<CancellationToken>()), times);

        [Fact]
        public async Task AnomalyStarted_RecordsAlarmEventWithLastReading()
        {
            await CreateService().HandleAsync("DEV1", Prediction(), isState: false, CancellationToken.None);
            VerifyStarted(Times.Once());
        }

        [Fact]
        public async Task Severity_IsMapped()
        {
            await CreateService().HandleAsync("DEV1", Prediction(severity: "critical"), isState: false, CancellationToken.None);
            VerifyStarted(Times.Once(), AlarmSeverity.Critical);
        }

        [Fact]
        public async Task AnomalyEnded_DoesNotRecordAnything()
        {
            await CreateService().HandleAsync("DEV1", Prediction(type: "anomaly_ended"), isState: false, CancellationToken.None);
            VerifyStarted(Times.Never());
        }

        [Theory]
        [InlineData(2)]
        [InlineData(0)]
        public async Task UnsupportedVersion_IsIgnored(int version)
        {
            await CreateService().HandleAsync("DEV1", Prediction(version: version), isState: false, CancellationToken.None);
            VerifyStarted(Times.Never());
        }

        [Fact]
        public async Task UnknownPanel_IsIgnored()
        {
            await CreateService().HandleAsync("DEV1", Prediction(sensorId: 5), isState: false, CancellationToken.None);
            _registry.Verify(r => r.ProcessPredictionStartedAsync(It.IsAny<Panel>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<AlarmSeverity>(), It.IsAny<float>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task InvalidJson_DoesNotThrow()
        {
            await CreateService().HandleAsync("DEV1", "not json", isState: false, CancellationToken.None);
            VerifyStarted(Times.Never());
        }

        [Fact]
        public async Task State_WithOpenFault_RecordsTheMissedStart()
        {
            const string state = """
                {"version": 1, "device_id": "DEV1", "sensor_id": 0,
                 "active": {"zone_id": "z1", "kind": "fault", "severity": "warning", "start": "2026-11-04T17:15:44Z"},
                 "timestamp": "2026-11-04T18:00:00Z"}
                """;
            await CreateService().HandleAsync("DEV1", state, isState: true, CancellationToken.None);
            VerifyStarted(Times.Once());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task EmptyPayload_ClearedRetainedState_IsIgnored(string? payload)
        {
            await CreateService().HandleAsync("DEV1", payload, isState: true, CancellationToken.None);
            VerifyStarted(Times.Never());
        }

        [Fact]
        public async Task State_WithoutOpenFault_DoesNothing()
        {
            const string state = """{"version": 1, "device_id": "DEV1", "sensor_id": 0, "active": null, "timestamp": "2026-11-04T18:00:00Z"}""";
            await CreateService().HandleAsync("DEV1", state, isState: true, CancellationToken.None);
            VerifyStarted(Times.Never());
        }

        [Fact]
        public async Task PushPrediction_IsProcessedByTheBackgroundLoop()
        {
            var service = CreateService();
            var processed = new TaskCompletionSource();
            _registry.Setup(r => r.ProcessPredictionStartedAsync(It.IsAny<Panel>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                    It.IsAny<AlarmSeverity>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
                .Callback(() => processed.TrySetResult())
                .ReturnsAsync(true);
            using var cts = new CancellationTokenSource();
            await service.StartAsync(cts.Token);

            service.PushPrediction("DEV1", Prediction(), isState: false);

            await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();
            await service.StopAsync(CancellationToken.None);
        }
    }
}
