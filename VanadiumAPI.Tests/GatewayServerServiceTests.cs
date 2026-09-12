using Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Models;
using Shared.Models.Mqtt;
using VanadiumAPI.DTO;
using VanadiumAPI.Services;
using VanadiumAPI.Services.DeviceCommands;
using VanadiumAPI.Services.PanelConfiguration;
using Xunit;

namespace VanadiumAPI.Tests
{
    public class GatewayServerServiceTests
    {
        private readonly Mock<IPanelInfoRepository> _repository = new();
        private readonly Mock<IHubBroadcastService> _hubBroadcast = new();
        private readonly Mock<IPanelConfigurationService> _panelConfig = new();
        private readonly Mock<IDeviceCommandService> _deviceCommands = new();

        private const int EnterpriseId = 1;

        public GatewayServerServiceTests()
        {
            // Every public method lazily initializes the in-memory store from this call.
            _repository.Setup(r => r.GetAllGatewaysAsync()).ReturnsAsync(new List<Gateway>());
        }

        private GatewayServerService CreateService()
        {
            var services = new ServiceCollection();
            services.AddSingleton(_repository.Object);
            services.AddSingleton(_panelConfig.Object);
            services.AddSingleton(_deviceCommands.Object);
            var provider = services.BuildServiceProvider();
            return new GatewayServerService(provider, _hubBroadcast.Object, new Mock<ILogger<GatewayServerService>>().Object);
        }

        // ---------- AddGatewayAsync ----------

        [Fact]
        public async Task AddGatewayAsync_MissingGatewayId_ReturnsError()
        {
            var service = CreateService();

            var (gatewayId, error) = await service.AddGatewayAsync(new AddGatewayDto { GatewayId = "" }, EnterpriseId);

            Assert.Null(gatewayId);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task AddGatewayAsync_DuplicateGatewayId_ReturnsError()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(new Gateway { GatewayId = "GW1" });
            var service = CreateService();

            var (gatewayId, error) = await service.AddGatewayAsync(new AddGatewayDto { GatewayId = "GW1" }, EnterpriseId);

            Assert.Null(gatewayId);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task AddGatewayAsync_Valid_PersistsAndBroadcastsGatewayAdded()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync((Gateway?)null);
            _repository.Setup(r => r.SaveAll()).ReturnsAsync(true);
            // Background GET_SENSORS refresh fired by AddGatewayAsync; make it fail harmlessly.
            _deviceCommands.Setup(d => d.GetSensorsAsync("GW1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(((GetSensorsData?)null, "offline"));
            var service = CreateService();

            var (gatewayId, error) = await service.AddGatewayAsync(new AddGatewayDto { GatewayId = "GW1" }, EnterpriseId);

            Assert.Equal("GW1", gatewayId);
            Assert.Null(error);
            _repository.Verify(r => r.Add(It.Is<Gateway>(g => g.GatewayId == "GW1" && g.EnterpriseId == EnterpriseId)), Times.Once);
            _hubBroadcast.Verify(b => b.BroadcastGatewayAdded(EnterpriseId, "GW1"), Times.Once);
        }

        // ---------- DeleteGatewayAsync ----------

        [Fact]
        public async Task DeleteGatewayAsync_NotFound_ReturnsError()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync((Gateway?)null);
            var service = CreateService();

            var (success, error) = await service.DeleteGatewayAsync("GW1", EnterpriseId);

            Assert.False(success);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task DeleteGatewayAsync_WrongEnterprise_ReturnsError()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(new Gateway { GatewayId = "GW1", EnterpriseId = 999 });
            var service = CreateService();

            var (success, error) = await service.DeleteGatewayAsync("GW1", EnterpriseId);

            Assert.False(success);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task DeleteGatewayAsync_Valid_DeletesAndBroadcasts()
        {
            var gateway = new Gateway { GatewayId = "GW1", EnterpriseId = EnterpriseId };
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(gateway);
            _repository.Setup(r => r.GetAllPanels(It.IsAny<System.Linq.Expressions.Expression<Func<Panel, bool>>>()))
                .ReturnsAsync(new List<Panel> { new() { Id = 1, GatewayId = "GW1" } });
            _repository.Setup(r => r.SaveAll()).ReturnsAsync(true);
            var service = CreateService();

            var (success, error) = await service.DeleteGatewayAsync("GW1", EnterpriseId);

            Assert.True(success);
            Assert.Null(error);
            _repository.Verify(r => r.Delete(gateway), Times.Once);
            _hubBroadcast.Verify(b => b.RemoveGateway("GW1", It.Is<IEnumerable<int>>(ids => ids.Contains(1))), Times.Once);
            _hubBroadcast.Verify(b => b.BroadcastGatewayRemoved(EnterpriseId, "GW1"), Times.Once);
        }

        // ---------- RefreshGatewaySensorsAsync ----------

        [Fact]
        public async Task RefreshGatewaySensorsAsync_DeviceOffline_ReturnsError()
        {
            _deviceCommands.Setup(d => d.GetSensorsAsync("GW1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(((GetSensorsData?)null, "Timeout aguardando resposta do dispositivo"));
            var service = CreateService();

            var (sensors, error) = await service.RefreshGatewaySensorsAsync("GW1", EnterpriseId);

            Assert.Null(sensors);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task RefreshGatewaySensorsAsync_Success_PersistsAvailableSensorsAndBroadcasts()
        {
            var data = new GetSensorsData
            {
                Sensors = new List<SensorInfo>
                {
                    new() { SensorId = 0, Type = "flow" },
                    new() { SensorId = 1, Type = "temperature" }
                }
            };
            _deviceCommands.Setup(d => d.GetSensorsAsync("GW1", It.IsAny<CancellationToken>())).ReturnsAsync((data, (string?)null));
            var gateway = new Gateway { GatewayId = "GW1", EnterpriseId = EnterpriseId };
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(gateway);
            _repository.Setup(r => r.SaveAll()).ReturnsAsync(true);
            var service = CreateService();

            var (sensors, error) = await service.RefreshGatewaySensorsAsync("GW1", EnterpriseId);

            Assert.Null(error);
            Assert.NotNull(sensors);
            Assert.Contains(PanelType.Flow, sensors!);
            Assert.Contains(PanelType.Temperature, sensors);
            Assert.True(gateway.IsSetup);
            _hubBroadcast.Verify(b => b.BroadcastGatewaySystemInfo(EnterpriseId, It.IsAny<SystemMessageModel>()), Times.Once);
        }

        // ---------- AddGatewaySystemInfoAsync (heartbeat) ----------

        [Fact]
        public async Task AddGatewaySystemInfoAsync_GatewayNotInDatabase_DoesNothing()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync((Gateway?)null);
            var service = CreateService();

            await service.AddGatewaySystemInfoAsync("GW1", new SystemMessageModel { GatewayId = "GW1" });

            _hubBroadcast.Verify(b => b.BroadcastGatewaySystemInfo(It.IsAny<int>(), It.IsAny<SystemMessageModel>()), Times.Never);
            _panelConfig.Verify(p => p.ResumePendingConfigurations(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task AddGatewaySystemInfoAsync_FirstHeartbeat_BroadcastsAndResumesPendingConfigurations()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(new Gateway { GatewayId = "GW1", EnterpriseId = EnterpriseId });
            var service = CreateService();

            await service.AddGatewaySystemInfoAsync("GW1", new SystemMessageModel { GatewayId = "GW1", IsConnected = true });

            _hubBroadcast.Verify(b => b.BroadcastGatewaySystemInfo(EnterpriseId, It.IsAny<SystemMessageModel>()), Times.Once);
            _panelConfig.Verify(p => p.ResumePendingConfigurations("GW1", EnterpriseId), Times.Once);
        }

        [Fact]
        public async Task AddGatewaySystemInfoAsync_AlreadyConnected_DoesNotRebroadcastOrResumeAgain()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(new Gateway { GatewayId = "GW1", EnterpriseId = EnterpriseId });
            var service = CreateService();

            await service.AddGatewaySystemInfoAsync("GW1", new SystemMessageModel { GatewayId = "GW1", IsConnected = true });
            await service.AddGatewaySystemInfoAsync("GW1", new SystemMessageModel { GatewayId = "GW1", IsConnected = true });

            _hubBroadcast.Verify(b => b.BroadcastGatewaySystemInfo(EnterpriseId, It.IsAny<SystemMessageModel>()), Times.Once);
            _panelConfig.Verify(p => p.ResumePendingConfigurations("GW1", EnterpriseId), Times.Once);
        }

        // ---------- CheckGatewayHeartbeatTimeoutsAsync ----------

        [Fact]
        public async Task CheckGatewayHeartbeatTimeoutsAsync_TimeoutElapsed_MarksOfflineAndBroadcasts()
        {
            // AddGatewaySystemInfoAsync always stamps LastActivity as "now", so a stale heartbeat is
            // simulated with a real (tiny) delay plus a near-zero timeout, rather than a backdated timestamp.
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(new Gateway { GatewayId = "GW1", EnterpriseId = EnterpriseId });
            var service = CreateService();
            await service.AddGatewaySystemInfoAsync("GW1", new SystemMessageModel { GatewayId = "GW1", IsConnected = true });
            _hubBroadcast.Invocations.Clear();
            await Task.Delay(20);

            await service.CheckGatewayHeartbeatTimeoutsAsync(TimeSpan.FromMilliseconds(1));

            var info = await service.GetGatewayInfoByEnterpriseAsync(EnterpriseId);
            Assert.False(info["GW1"].IsConnected);
            _hubBroadcast.Verify(b => b.BroadcastGatewaySystemInfo(EnterpriseId, It.Is<SystemMessageModel>(s => !s.IsConnected)), Times.Once);
        }

        [Fact]
        public async Task CheckGatewayHeartbeatTimeoutsAsync_WithinTimeout_StaysOnline()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(new Gateway { GatewayId = "GW1", EnterpriseId = EnterpriseId });
            var service = CreateService();
            await service.AddGatewaySystemInfoAsync("GW1", new SystemMessageModel { GatewayId = "GW1", IsConnected = true });
            _hubBroadcast.Invocations.Clear();

            await service.CheckGatewayHeartbeatTimeoutsAsync(TimeSpan.FromMinutes(5));

            var info = await service.GetGatewayInfoByEnterpriseAsync(EnterpriseId);
            Assert.True(info["GW1"].IsConnected);
            _hubBroadcast.Verify(b => b.BroadcastGatewaySystemInfo(It.IsAny<int>(), It.IsAny<SystemMessageModel>()), Times.Never);
        }
    }
}
