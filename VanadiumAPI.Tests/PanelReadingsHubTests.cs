using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Models;
using VanadiumAPI.DTO;
using VanadiumAPI.Hubs;
using VanadiumAPI.Services;
using VanadiumAPI.Services.AlarmRegistry;
using VanadiumAPI.SensorDataSaver;
using Xunit;

namespace VanadiumAPI.Tests
{
    public class PanelReadingsHubTests
    {
        private readonly Mock<ISensorInfoService> _sensorInfoService = new();
        private readonly Mock<IPanelReadingService> _panelReadingService = new();
        private readonly Mock<IAuthService> _authService = new();
        private readonly Mock<IHubBroadcastService> _broadcastService = new();
        private readonly Mock<IGatewayServerService> _gatewayServer = new();
        private readonly Mock<IPanelService> _panelService = new();
        private readonly Mock<ISensorDataSaver> _sensorDataSaver = new();
        private readonly Mock<IAlarmRegistryService> _alarmRegistry = new();

        private readonly Mock<IHubCallerClients> _clients = new();
        private readonly Mock<ISingleClientProxy> _callerProxy = new();
        private readonly Mock<HubCallerContext> _context = new();

        private const string ConnectionId = "conn-1";
        private const int EnterpriseId = 7;

        private PanelReadingsHub CreateHub()
        {
            _clients.Setup(c => c.Caller).Returns(_callerProxy.Object);
            _context.Setup(c => c.ConnectionId).Returns(ConnectionId);

            var hub = new PanelReadingsHub(
                _sensorInfoService.Object,
                _panelReadingService.Object,
                _authService.Object,
                _broadcastService.Object,
                _gatewayServer.Object,
                _panelService.Object,
                _sensorDataSaver.Object,
                _alarmRegistry.Object,
                new Mock<ILogger<PanelReadingsHub>>().Object)
            {
                Clients = _clients.Object,
                Context = _context.Object
            };
            return hub;
        }

        private static string BuildToken(int userId, UserType userType)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("UserType", userType.ToString())
            };
            var token = new JwtSecurityToken(claims: claims);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private void AssertErrorSentToCaller() =>
            _callerProxy.Verify(p => p.SendCoreAsync("Error", It.IsAny<object[]>(), default), Times.Once);

        // ---------- GetAllPanels / GetPanelById (read-only, any authenticated user) ----------

        [Fact]
        public async Task GetAllPanels_InvalidToken_ReturnsEmpty()
        {
            _authService.Setup(a => a.ValidateTokenAsync(It.IsAny<string>())).ReturnsAsync(false);
            var hub = CreateHub();

            var result = await hub.GetAllPanels("bad-token");

            Assert.Empty(result);
            _sensorInfoService.Verify(s => s.GetAllPanelsAsync(), Times.Never);
        }

        [Fact]
        public async Task GetAllPanels_ValidToken_ReturnsMappedPanels()
        {
            _authService.Setup(a => a.ValidateTokenAsync("good-token")).ReturnsAsync(true);
            _sensorInfoService.Setup(s => s.GetAllPanelsAsync())
                .ReturnsAsync(new List<Panel> { new() { Id = 1, Name = "P1", GatewayId = "GW1", Index = "0", Color = "", Alarms = new List<Alarm>() } });
            var hub = CreateHub();

            var result = (await hub.GetAllPanels("good-token")).ToList();

            Assert.Single(result);
            Assert.Equal(1, result[0].Id);
        }

        [Fact]
        public async Task GetPanelById_ValidToken_ReturnsPanel()
        {
            _authService.Setup(a => a.ValidateTokenAsync("good-token")).ReturnsAsync(true);
            _sensorInfoService.Setup(s => s.GetPanelByIdAsync(5))
                .ReturnsAsync(new Panel { Id = 5, Name = "P5", GatewayId = "GW1", Index = "1", Color = "", Alarms = new List<Alarm>() });
            var hub = CreateHub();

            var result = await hub.GetPanelById(5, "good-token");

            Assert.NotNull(result);
            Assert.Equal(5, result!.Id);
        }

        [Fact]
        public async Task GetPanelById_UnknownId_ReturnsNull()
        {
            _authService.Setup(a => a.ValidateTokenAsync("good-token")).ReturnsAsync(true);
            _sensorInfoService.Setup(s => s.GetPanelByIdAsync(It.IsAny<int>())).ReturnsAsync((Panel?)null);
            var hub = CreateHub();

            var result = await hub.GetPanelById(999, "good-token");

            Assert.Null(result);
        }

        // ---------- AddPanel ----------

        [Fact]
        public async Task AddPanel_RegularUser_IsRejectedWithoutCallingService()
        {
            var token = BuildToken(1, UserType.User);
            _authService.Setup(a => a.ValidateTokenAsync(token)).ReturnsAsync(true);
            var hub = CreateHub();

            var result = await hub.AddPanel(new AddPanelDto(), token);

            Assert.Null(result);
            AssertErrorSentToCaller();
            _panelService.Verify(p => p.AddPanelAsync(It.IsAny<AddPanelDto>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task AddPanel_NoEnterpriseSelected_ReturnsError()
        {
            var token = BuildToken(1, UserType.Admin);
            _authService.Setup(a => a.ValidateTokenAsync(token)).ReturnsAsync(true);
            _broadcastService.Setup(b => b.GetConnectionEnterpriseId(ConnectionId)).Returns((int?)null);
            var hub = CreateHub();

            var result = await hub.AddPanel(new AddPanelDto(), token);

            Assert.Null(result);
            AssertErrorSentToCaller();
        }

        [Fact]
        public async Task AddPanel_ServiceReturnsError_SendsErrorToCallerAndReturnsNull()
        {
            var token = BuildToken(1, UserType.Manager);
            _authService.Setup(a => a.ValidateTokenAsync(token)).ReturnsAsync(true);
            _broadcastService.Setup(b => b.GetConnectionEnterpriseId(ConnectionId)).Returns(EnterpriseId);
            _panelService.Setup(p => p.AddPanelAsync(It.IsAny<AddPanelDto>(), EnterpriseId))
                .ReturnsAsync(((PanelDto?)null, "Gateway não encontrado"));
            var hub = CreateHub();

            var result = await hub.AddPanel(new AddPanelDto(), token);

            Assert.Null(result);
            AssertErrorSentToCaller();
        }

        [Fact]
        public async Task AddPanel_Admin_ValidRequest_ReturnsCreatedPanel()
        {
            var token = BuildToken(1, UserType.Admin);
            _authService.Setup(a => a.ValidateTokenAsync(token)).ReturnsAsync(true);
            _broadcastService.Setup(b => b.GetConnectionEnterpriseId(ConnectionId)).Returns(EnterpriseId);
            var expected = new PanelDto { Id = 10, Name = "New Panel" };
            var dto = new AddPanelDto { GatewayId = "GW1", Index = "0" };
            _panelService.Setup(p => p.AddPanelAsync(dto, EnterpriseId)).ReturnsAsync((expected, (string?)null));
            var hub = CreateHub();

            var result = await hub.AddPanel(dto, token);

            Assert.NotNull(result);
            Assert.Equal(10, result!.Id);
            _callerProxy.Verify(p => p.SendCoreAsync("Error", It.IsAny<object[]>(), default), Times.Never);
        }

        // ---------- UpdatePanel ----------

        [Fact]
        public async Task UpdatePanel_RegularUser_IsRejectedWithoutCallingService()
        {
            var token = BuildToken(1, UserType.User);
            _authService.Setup(a => a.ValidateTokenAsync(token)).ReturnsAsync(true);
            var hub = CreateHub();

            var result = await hub.UpdatePanel(new UpdatePanelDto { Id = 1 }, token);

            Assert.Null(result);
            AssertErrorSentToCaller();
            _panelService.Verify(p => p.UpdatePanelAsync(It.IsAny<UpdatePanelDto>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task UpdatePanel_Manager_ValidRequest_ReturnsUpdatedPanel()
        {
            var token = BuildToken(2, UserType.Manager);
            _authService.Setup(a => a.ValidateTokenAsync(token)).ReturnsAsync(true);
            _broadcastService.Setup(b => b.GetConnectionEnterpriseId(ConnectionId)).Returns(EnterpriseId);
            var dto = new UpdatePanelDto { Id = 1, Name = "Renamed" };
            var expected = new PanelDto { Id = 1, Name = "Renamed" };
            _panelService.Setup(p => p.UpdatePanelAsync(dto, EnterpriseId)).ReturnsAsync((expected, (string?)null));
            var hub = CreateHub();

            var result = await hub.UpdatePanel(dto, token);

            Assert.NotNull(result);
            Assert.Equal("Renamed", result!.Name);
        }

        // ---------- DeletePanel ----------

        [Fact]
        public async Task DeletePanel_RegularUser_IsRejectedWithoutCallingService()
        {
            var token = BuildToken(1, UserType.User);
            _authService.Setup(a => a.ValidateTokenAsync(token)).ReturnsAsync(true);
            var hub = CreateHub();

            var result = await hub.DeletePanel(1, token);

            Assert.False(result);
            AssertErrorSentToCaller();
            _panelService.Verify(p => p.DeletePanelAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task DeletePanel_Admin_ServiceSucceeds_ReturnsTrue()
        {
            var token = BuildToken(1, UserType.Admin);
            _authService.Setup(a => a.ValidateTokenAsync(token)).ReturnsAsync(true);
            _broadcastService.Setup(b => b.GetConnectionEnterpriseId(ConnectionId)).Returns(EnterpriseId);
            _panelService.Setup(p => p.DeletePanelAsync(1, EnterpriseId)).ReturnsAsync((true, (string?)null));
            var hub = CreateHub();

            var result = await hub.DeletePanel(1, token);

            Assert.True(result);
        }

        [Fact]
        public async Task DeletePanel_ServiceReturnsError_SendsErrorAndReturnsFalse()
        {
            var token = BuildToken(1, UserType.Admin);
            _authService.Setup(a => a.ValidateTokenAsync(token)).ReturnsAsync(true);
            _broadcastService.Setup(b => b.GetConnectionEnterpriseId(ConnectionId)).Returns(EnterpriseId);
            _panelService.Setup(p => p.DeletePanelAsync(1, EnterpriseId)).ReturnsAsync((false, "Painel não encontrado"));
            var hub = CreateHub();

            var result = await hub.DeletePanel(1, token);

            Assert.False(result);
            AssertErrorSentToCaller();
        }
    }
}
