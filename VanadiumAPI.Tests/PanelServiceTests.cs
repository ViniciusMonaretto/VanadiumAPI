using System.Linq.Expressions;
using Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Models;
using Shared.Models.Mqtt;
using VanadiumAPI.DTO;
using VanadiumAPI.Services;
using VanadiumAPI.Services.AlarmRegistry;
using VanadiumAPI.Services.PanelConfiguration;
using VanadiumAPI.SensorDataSaver;
using Xunit;

namespace VanadiumAPI.Tests
{
    public class PanelServiceTests
    {
        private readonly Mock<IPanelInfoRepository> _repository = new();
        private readonly Mock<IHubBroadcastService> _broadcastService = new();
        private readonly Mock<ISensorDataSaver> _sensorDataSaver = new();
        private readonly Mock<IAlarmRegistryService> _alarmRegistry = new();
        private readonly Mock<IGatewayServerService> _gatewayServer = new();
        private readonly Mock<IDeviceInfoStore> _deviceInfoStore = new();
        private readonly Mock<IPanelConfigurationService> _panelConfig = new();

        private const int EnterpriseId = 1;

        private PanelService CreateService() => new(
            _repository.Object,
            _broadcastService.Object,
            _sensorDataSaver.Object,
            _alarmRegistry.Object,
            _gatewayServer.Object,
            _deviceInfoStore.Object,
            _panelConfig.Object,
            new Mock<ILogger<PanelService>>().Object);

        private static Gateway MakeGateway(string gatewayId = "GW1", int enterpriseId = EnterpriseId, bool isSetup = true, params PanelType[] availableSensors) => new()
        {
            GatewayId = gatewayId,
            EnterpriseId = enterpriseId,
            IsSetup = isSetup,
            AvailableSensors = availableSensors.ToList()
        };

        private static Group MakeGroup(int id = 1, int enterpriseId = EnterpriseId) => new()
        {
            Id = id,
            Name = "Group",
            EnterpriseId = enterpriseId
        };

        private static AddPanelDto MakeAddDto(string gatewayId = "GW1", string index = "0", int groupId = 1, PanelType type = PanelType.Flow) => new()
        {
            Name = "Panel",
            GatewayId = gatewayId,
            GroupId = groupId,
            Index = index,
            Color = "#fff",
            Gain = 1,
            Offset = 0,
            Multiplier = 1,
            SamplingMs = 1000,
            Enabled = true,
            Type = type
        };

        // ---------- AddPanelAsync ----------

        [Fact]
        public async Task AddPanelAsync_MissingGatewayOrIndex_ReturnsError()
        {
            var service = CreateService();

            var (panel, error) = await service.AddPanelAsync(new AddPanelDto { GatewayId = "", Index = "0" }, EnterpriseId);

            Assert.Null(panel);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task AddPanelAsync_NonNumericIndex_ReturnsError()
        {
            var service = CreateService();

            var (panel, error) = await service.AddPanelAsync(MakeAddDto(index: "abc"), EnterpriseId);

            Assert.Null(panel);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task AddPanelAsync_GatewayNotFound_ReturnsError()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync((Gateway?)null);
            var service = CreateService();

            var (panel, error) = await service.AddPanelAsync(MakeAddDto(), EnterpriseId);

            Assert.Null(panel);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task AddPanelAsync_GatewayBelongsToAnotherEnterprise_ReturnsError()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway(enterpriseId: 999));
            var service = CreateService();

            var (panel, error) = await service.AddPanelAsync(MakeAddDto(), EnterpriseId);

            Assert.Null(panel);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task AddPanelAsync_GroupNotFound_ReturnsError()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway());
            _repository.Setup(r => r.GetGroupById(1)).ReturnsAsync((Group?)null);
            var service = CreateService();

            var (panel, error) = await service.AddPanelAsync(MakeAddDto(), EnterpriseId);

            Assert.Null(panel);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task AddPanelAsync_DuplicateGatewayAndIndex_ReturnsError()
        {
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway());
            _repository.Setup(r => r.GetGroupById(1)).ReturnsAsync(MakeGroup());
            _repository.Setup(r => r.GetPanelByGatewayAndIndexAsync("GW1", "0")).ReturnsAsync(new Panel { Id = 5 });
            var service = CreateService();

            var (panel, error) = await service.AddPanelAsync(MakeAddDto(), EnterpriseId);

            Assert.Null(panel);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task AddPanelAsync_SensorTypeMismatch_ReturnsError()
        {
            // Gateway reports "Flow" as available, but the panel is being added as "Temperature".
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway(availableSensors: PanelType.Flow));
            _repository.Setup(r => r.GetGroupById(1)).ReturnsAsync(MakeGroup());
            _repository.Setup(r => r.GetPanelByGatewayAndIndexAsync("GW1", "0")).ReturnsAsync((Panel?)null);
            _deviceInfoStore.Setup(d => d.GetSensorCapabilities("GW1")).Returns((IReadOnlyList<SensorInfo>?)null);
            var service = CreateService();

            var (panel, error) = await service.AddPanelAsync(MakeAddDto(type: PanelType.Temperature), EnterpriseId);

            Assert.Null(panel);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task AddPanelAsync_ValidRequest_CreatesPanelAndStartsDeviceEnable()
        {
            var gateway = MakeGateway(availableSensors: PanelType.Flow);
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(gateway);
            _repository.Setup(r => r.GetGroupById(1)).ReturnsAsync(MakeGroup());
            _repository.Setup(r => r.GetPanelByGatewayAndIndexAsync("GW1", "0")).ReturnsAsync((Panel?)null);
            _deviceInfoStore.Setup(d => d.GetSensorCapabilities("GW1")).Returns((IReadOnlyList<SensorInfo>?)null);

            Panel? createdPanel = null;
            _repository.Setup(r => r.CreatePanelAsync(It.IsAny<Panel>()))
                .Callback<Panel>(p => { p.Id = 42; createdPanel = p; })
                .ReturnsAsync(() => createdPanel);

            var service = CreateService();

            var (panel, error) = await service.AddPanelAsync(MakeAddDto(type: PanelType.Flow), EnterpriseId);

            Assert.Null(error);
            Assert.NotNull(panel);
            Assert.Equal(42, panel!.Id);
            Assert.True(panel.Configuring);

            _sensorDataSaver.Verify(s => s.AddPanel(It.Is<Panel>(p => p.Id == 42)), Times.Once);
            _broadcastService.Verify(b => b.AddPanel(EnterpriseId, 42, "GW1"), Times.Once);
            _broadcastService.Verify(b => b.BroadcastPanelAdded(EnterpriseId, It.IsAny<PanelDto>()), Times.Once);
            _panelConfig.Verify(p => p.StartEnable(It.Is<Panel>(pp => pp.Id == 42), EnterpriseId, 0), Times.Once);
        }

        // ---------- UpdatePanelAsync ----------

        [Fact]
        public async Task UpdatePanelAsync_PanelNotFound_ReturnsError()
        {
            _repository.Setup(r => r.GetPanelById(1)).ReturnsAsync((Panel?)null);
            var service = CreateService();

            var (panel, error) = await service.UpdatePanelAsync(new UpdatePanelDto { Id = 1 }, EnterpriseId);

            Assert.Null(panel);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task UpdatePanelAsync_WrongEnterprise_ReturnsError()
        {
            _repository.Setup(r => r.GetPanelById(1)).ReturnsAsync(new Panel { Id = 1, GatewayId = "GW1" });
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway(enterpriseId: 999));
            var service = CreateService();

            var (panel, error) = await service.UpdatePanelAsync(new UpdatePanelDto { Id = 1 }, EnterpriseId);

            Assert.Null(panel);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task UpdatePanelAsync_PanelInFlight_ReturnsError()
        {
            _repository.Setup(r => r.GetPanelById(1)).ReturnsAsync(new Panel { Id = 1, GatewayId = "GW1" });
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway());
            _panelConfig.Setup(p => p.IsInFlight(1)).Returns(true);
            var service = CreateService();

            var (panel, error) = await service.UpdatePanelAsync(new UpdatePanelDto { Id = 1 }, EnterpriseId);

            Assert.Null(panel);
            Assert.NotNull(error);
            _repository.Verify(r => r.UpdatePanelAsync(It.IsAny<Panel>()), Times.Never);
        }

        [Fact]
        public async Task UpdatePanelAsync_ValidRequest_UpdatesFieldsAndBroadcasts()
        {
            var existing = new Panel { Id = 1, GatewayId = "GW1", Name = "Old", Gain = 1, Offset = 0, Enabled = true, Alarms = new List<Alarm>() };
            _repository.Setup(r => r.GetPanelById(1)).ReturnsAsync(existing);
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway());
            _panelConfig.Setup(p => p.IsInFlight(1)).Returns(false);
            _repository.Setup(r => r.UpdatePanelAsync(It.IsAny<Panel>())).ReturnsAsync(true);
            _repository.Setup(r => r.SaveAll()).ReturnsAsync(true);

            var dto = new UpdatePanelDto { Id = 1, Name = "New", Gain = 2.5f, Enabled = false };
            var service = CreateService();

            var (panel, error) = await service.UpdatePanelAsync(dto, EnterpriseId);

            Assert.Null(error);
            Assert.NotNull(panel);
            Assert.Equal("New", existing.Name);
            Assert.Equal(2.5f, existing.Gain);
            Assert.False(existing.Enabled);
            _sensorDataSaver.Verify(s => s.UpdatePanel(existing), Times.Once);
            _broadcastService.Verify(b => b.BroadcastPanelChange(PanelChangeAction.Update, It.IsAny<PanelDto>()), Times.Once);
        }

        // ---------- DeletePanelAsync ----------

        [Fact]
        public async Task DeletePanelAsync_PanelNotFound_ReturnsError()
        {
            _repository.Setup(r => r.GetPanelById(1)).ReturnsAsync((Panel?)null);
            var service = CreateService();

            var (success, error) = await service.DeletePanelAsync(1, EnterpriseId);

            Assert.False(success);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task DeletePanelAsync_PanelInFlight_ReturnsError()
        {
            _repository.Setup(r => r.GetPanelById(1)).ReturnsAsync(new Panel { Id = 1, GatewayId = "GW1" });
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway());
            _panelConfig.Setup(p => p.IsInFlight(1)).Returns(true);
            var service = CreateService();

            var (success, error) = await service.DeletePanelAsync(1, EnterpriseId);

            Assert.False(success);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task DeletePanelAsync_StillConfiguring_PurgesLocallyWithoutDeviceRoundTrip()
        {
            var panel = new Panel { Id = 1, GatewayId = "GW1", Index = "0", Configuring = true };
            _repository.Setup(r => r.GetPanelById(1)).ReturnsAsync(panel);
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway());
            _panelConfig.Setup(p => p.IsInFlight(1)).Returns(false);
            _repository.Setup(r => r.DeletePanelAsync(panel)).ReturnsAsync(true);
            var service = CreateService();

            var (success, error) = await service.DeletePanelAsync(1, EnterpriseId);

            Assert.True(success);
            Assert.Null(error);
            _panelConfig.Verify(p => p.StartDisableAndDelete(It.IsAny<Panel>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            _broadcastService.Verify(b => b.BroadcastPanelRemoved(EnterpriseId, 1), Times.Once);
        }

        [Fact]
        public async Task DeletePanelAsync_GatewayOffline_PurgesLocallyWithoutDeviceRoundTrip()
        {
            var panel = new Panel { Id = 1, GatewayId = "GW1", Index = "0", Configuring = false };
            _repository.Setup(r => r.GetPanelById(1)).ReturnsAsync(panel);
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway());
            _panelConfig.Setup(p => p.IsInFlight(1)).Returns(false);
            _gatewayServer.Setup(g => g.GetGatewayInfoByEnterpriseAsync(EnterpriseId))
                .ReturnsAsync(new Dictionary<string, SystemMessageModel>
                {
                    ["GW1"] = new SystemMessageModel { GatewayId = "GW1", IsConnected = false }
                });
            _repository.Setup(r => r.DeletePanelAsync(panel)).ReturnsAsync(true);
            var service = CreateService();

            var (success, error) = await service.DeletePanelAsync(1, EnterpriseId);

            Assert.True(success);
            Assert.Null(error);
            _panelConfig.Verify(p => p.StartDisableAndDelete(It.IsAny<Panel>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task DeletePanelAsync_GatewayOnline_MarksConfiguringAndStartsDeviceDisable()
        {
            var panel = new Panel { Id = 1, GatewayId = "GW1", Index = "3", Configuring = false };
            _repository.Setup(r => r.GetPanelById(1)).ReturnsAsync(panel);
            _repository.Setup(r => r.GetGatewayByGatewayIdAsync("GW1")).ReturnsAsync(MakeGateway());
            _panelConfig.Setup(p => p.IsInFlight(1)).Returns(false);
            _gatewayServer.Setup(g => g.GetGatewayInfoByEnterpriseAsync(EnterpriseId))
                .ReturnsAsync(new Dictionary<string, SystemMessageModel>
                {
                    ["GW1"] = new SystemMessageModel { GatewayId = "GW1", IsConnected = true }
                });
            _repository.Setup(r => r.UpdatePanelAsync(panel)).ReturnsAsync(true);
            var service = CreateService();

            var (success, error) = await service.DeletePanelAsync(1, EnterpriseId);

            Assert.True(success);
            Assert.Null(error);
            Assert.True(panel.Configuring);
            _repository.Verify(r => r.DeletePanelAsync(It.IsAny<Panel>()), Times.Never);
            _panelConfig.Verify(p => p.StartDisableAndDelete(panel, EnterpriseId, 3), Times.Once);
        }
    }
}
