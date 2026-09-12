using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Models;
using VanadiumAPI.DTO;
using VanadiumAPI.Hubs;
using VanadiumAPI.Services;
using Xunit;

namespace VanadiumAPI.Tests
{
    public class HubBroadcastServiceTests : IAsyncDisposable
    {
        private readonly Mock<IHubClients> _hubClients = new();
        private readonly Mock<ISingleClientProxy> _clientProxy = new();
        private readonly HubBroadcastService _service;

        private const int EnterpriseId = 1;
        private const string ConnectionId = "conn-1";

        public HubBroadcastServiceTests()
        {
            var hubContext = new Mock<IHubContext<PanelReadingsHub>>();
            hubContext.Setup(h => h.Clients).Returns(_hubClients.Object);
            _hubClients.Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>())).Returns(_clientProxy.Object);
            _hubClients.Setup(c => c.Client(It.IsAny<string>())).Returns(_clientProxy.Object);
            _clientProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _service = new HubBroadcastService(hubContext.Object, new Mock<ILogger<HubBroadcastService>>().Object);
        }

        public async ValueTask DisposeAsync() => await _service.DisposeAsync();

        private void AssertSent(string method, Times times) =>
            _clientProxy.Verify(p => p.SendCoreAsync(method, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), times);

        // ---------- Panel subscriptions ----------

        [Fact]
        public async Task BroadcastPanelChange_SubscribedConnection_Sends()
        {
            _service.SubscribeToPanel(ConnectionId, userId: 1, panelId: 10, gatewayId: "GW1");

            await _service.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto { Id = 10 });

            AssertSent("PanelChangeReceived", Times.Once());
        }

        [Fact]
        public async Task BroadcastPanelChange_NoSubscribers_DoesNotSend()
        {
            await _service.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto { Id = 999 });

            AssertSent("PanelChangeReceived", Times.Never());
        }

        [Fact]
        public async Task UnsubscribeFromPanel_StopsFurtherBroadcasts()
        {
            _service.SubscribeToPanel(ConnectionId, userId: 1, panelId: 10, gatewayId: "GW1");
            _service.UnsubscribeFromPanel(ConnectionId, panelId: 10, gatewayId: "GW1");

            await _service.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto { Id = 10 });

            AssertSent("PanelChangeReceived", Times.Never());
        }

        [Fact]
        public void SetUserPanels_SubscribesToEachGivenPanelAndTracksEnterprise()
        {
            _service.SetUserPanels(ConnectionId, userId: 1, enterpriseId: EnterpriseId,
                panelGatewayIds: new[] { (1, "GW1"), (2, "GW2") });

            Assert.Equal(EnterpriseId, _service.GetConnectionEnterpriseId(ConnectionId));
        }

        [Fact]
        public void SetUserPanels_ReplacesPreviousSubscriptions()
        {
            _service.SetUserPanels(ConnectionId, userId: 1, enterpriseId: EnterpriseId, panelGatewayIds: new[] { (1, "GW1") });
            _service.SetUserPanels(ConnectionId, userId: 1, enterpriseId: EnterpriseId, panelGatewayIds: new[] { (2, "GW2") });

            // Old subscription (panel 1) must no longer receive broadcasts after being replaced.
            _service.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto { Id = 1 }).GetAwaiter().GetResult();
            AssertSent("PanelChangeReceived", Times.Never());
        }

        [Fact]
        public void GetConnectionEnterpriseId_UnknownConnection_ReturnsNull()
        {
            Assert.Null(_service.GetConnectionEnterpriseId("unknown"));
        }

        [Fact]
        public async Task RemoveConnection_ClearsPanelAndEnterpriseSubscriptions()
        {
            _service.SetUserPanels(ConnectionId, userId: 1, enterpriseId: EnterpriseId, panelGatewayIds: new[] { (1, "GW1") });

            _service.RemoveConnection(ConnectionId);

            Assert.Null(_service.GetConnectionEnterpriseId(ConnectionId));
            await _service.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto { Id = 1 });
            AssertSent("PanelChangeReceived", Times.Never());
        }

        // ---------- Enterprise-wide broadcasts ----------

        [Fact]
        public async Task BroadcastGatewaySystemInfo_OnlySendsToConnectionsOnThatEnterprise()
        {
            _service.SetUserPanels(ConnectionId, userId: 1, enterpriseId: EnterpriseId, panelGatewayIds: Array.Empty<(int, string)>());

            await _service.BroadcastGatewaySystemInfo(EnterpriseId, new SystemMessageModel { GatewayId = "GW1" });
            await _service.BroadcastGatewaySystemInfo(enterpriseId: 999, new SystemMessageModel { GatewayId = "GW1" });

            AssertSent("GatewaySystemInfoReceived", Times.Once());
        }

        [Fact]
        public async Task AddPanel_SubscribesEveryConnectionOnEnterpriseToTheNewPanel()
        {
            _service.SetUserPanels(ConnectionId, userId: 1, enterpriseId: EnterpriseId, panelGatewayIds: Array.Empty<(int, string)>());

            _service.AddPanel(EnterpriseId, panelId: 42, gatewayId: "GW1");
            await _service.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto { Id = 42 });

            AssertSent("PanelChangeReceived", Times.Once());
        }

        [Fact]
        public async Task RemovePanel_UnsubscribesAllConnectionsFromThatPanel()
        {
            _service.SubscribeToPanel(ConnectionId, userId: 1, panelId: 42, gatewayId: "GW1");

            _service.RemovePanel(panelId: 42, gatewayId: "GW1");
            await _service.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto { Id = 42 });

            AssertSent("PanelChangeReceived", Times.Never());
        }

        [Fact]
        public async Task BroadcastPanelAdded_SendsToEnterpriseConnections()
        {
            _service.SetUserPanels(ConnectionId, userId: 1, enterpriseId: EnterpriseId, panelGatewayIds: Array.Empty<(int, string)>());

            await _service.BroadcastPanelAdded(EnterpriseId, new PanelDto { Id = 1 });

            AssertSent("PanelAdded", Times.Once());
        }

        [Fact]
        public async Task BroadcastPanelRemoved_SendsToEnterpriseConnections()
        {
            _service.SetUserPanels(ConnectionId, userId: 1, enterpriseId: EnterpriseId, panelGatewayIds: Array.Empty<(int, string)>());

            await _service.BroadcastPanelRemoved(EnterpriseId, panelId: 1);

            AssertSent("PanelRemoved", Times.Once());
        }

        [Fact]
        public async Task BroadcastError_SendsToEnterpriseConnections()
        {
            _service.SetUserPanels(ConnectionId, userId: 1, enterpriseId: EnterpriseId, panelGatewayIds: Array.Empty<(int, string)>());

            await _service.BroadcastError(EnterpriseId, "something failed");

            AssertSent("Error", Times.Once());
        }

        [Fact]
        public async Task BroadcastGatewayAdded_And_BroadcastGatewayRemoved_SendToEnterpriseConnections()
        {
            _service.SetUserPanels(ConnectionId, userId: 1, enterpriseId: EnterpriseId, panelGatewayIds: Array.Empty<(int, string)>());

            await _service.BroadcastGatewayAdded(EnterpriseId, "GW1");
            await _service.BroadcastGatewayRemoved(EnterpriseId, "GW1");

            AssertSent("GatewayAdded", Times.Once());
            AssertSent("GatewayRemoved", Times.Once());
        }
    }
}
