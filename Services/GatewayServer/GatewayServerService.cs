using System.Collections.Concurrent;
using Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Mqtt;
using VanadiumAPI.DTO;
using VanadiumAPI.Services.DeviceCommands;

namespace VanadiumAPI.Services
{
    public class GatewayServerService : IGatewayServerService
    {
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, SystemMessageModel>> _store = new();
        private readonly IServiceProvider _provider;
        private readonly IHubBroadcastService _hubBroadcast;
        private readonly ILogger<GatewayServerService> _logger;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _initialized;

        public GatewayServerService(IServiceProvider provider, IHubBroadcastService hubBroadcast, ILogger<GatewayServerService> logger)
        {
            _provider = provider;
            _hubBroadcast = hubBroadcast;
            _logger = logger;
        }

        public async Task InitializeStoreAsync() => await EnsureStoreInitializedAsync();

        private async Task EnsureStoreInitializedAsync()
        {
            if (_initialized) return;
            await _initLock.WaitAsync();
            try
            {
                if (_initialized) return;
                using var scope = _provider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                var gateways = await repo.GetAllGatewaysAsync();
                foreach (var g in gateways)
                {
                    var perEnterprise = _store.GetOrAdd(g.EnterpriseId, _ => new ConcurrentDictionary<string, SystemMessageModel>());
                    perEnterprise[g.GatewayId] = new SystemMessageModel
                    {
                        GatewayId = g.GatewayId,
                        IsConnected = false,
                        Uptime = null,
                        IpAddress = null,
                        AvailableSensors = g.AvailableSensors ?? new List<PanelType>()
                    };
                }
                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<(string? GatewayId, string? Error)> AddGatewayAsync(AddGatewayDto dto, int enterpriseId)
        {
            if (string.IsNullOrWhiteSpace(dto?.GatewayId))
                return (null, "GatewayId é obrigatório");

            var gatewayId = dto.GatewayId.Trim();
            Gateway? existing;
            using (var scope = _provider.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                existing = await repo.GetGatewayByGatewayIdAsync(gatewayId);
            }
            if (existing != null)
                return (null, "Já existe um gateway com esse identificador");

            var gateway = new Gateway { GatewayId = gatewayId, EnterpriseId = enterpriseId };
            using (var scope = _provider.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                repo.Add(gateway);
                if (!await repo.SaveAll())
                    return (null, "Erro ao salvar o gateway");
            }

            await AddGatewayToStoreAsync(enterpriseId, gatewayId);
            await _hubBroadcast.BroadcastGatewayAdded(enterpriseId, gatewayId);
            _ = VerifyGatewaySensorsAsync(gatewayId, enterpriseId);
            return (gatewayId, null);
        }

        /// <summary>Asks the device (via GET_SENSORS) which sensors it has, persists them on the Gateway and broadcasts the update.</summary>
        private async Task VerifyGatewaySensorsAsync(string gatewayId, int enterpriseId)
        {
            try
            {
                (GetSensorsData? Data, string? Error) result;
                using (var scope = _provider.CreateScope())
                {
                    var commandService = scope.ServiceProvider.GetRequiredService<IDeviceCommandService>();
                    result = await commandService.GetSensorsAsync(gatewayId);
                }

                if (result.Data == null)
                {
                    _logger.LogWarning("GET_SENSORS failed for newly added gateway {GatewayId}: {Error}", gatewayId, result.Error);
                    return;
                }

                var availableSensors = result.Data.Sensors
                    .Select(s => SensorTypeMapper.ToPanelType(s.Type))
                    .Where(t => t.HasValue)
                    .Select(t => t!.Value)
                    .Distinct()
                    .ToList();

                using (var scope = _provider.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                    var gateway = await repo.GetGatewayByGatewayIdAsync(gatewayId);
                    if (gateway == null)
                    {
                        _logger.LogWarning("Gateway {GatewayId} no longer exists; discarding GET_SENSORS result", gatewayId);
                        return;
                    }
                    gateway.AvailableSensors = availableSensors;
                    gateway.IsSetup = true;
                    await repo.SaveAll();
                }

                await EnsureStoreInitializedAsync();
                var perEnterprise = _store.GetOrAdd(enterpriseId, _ => new ConcurrentDictionary<string, SystemMessageModel>());
                var systemData = perEnterprise.AddOrUpdate(
                    gatewayId,
                    _ => new SystemMessageModel { GatewayId = gatewayId, IsConnected = false, AvailableSensors = availableSensors },
                    (_, existing) => { existing.AvailableSensors = availableSensors; return existing; });

                await _hubBroadcast.BroadcastGatewaySystemInfo(enterpriseId, systemData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying sensors for gateway {GatewayId}", gatewayId);
            }
        }

        public async Task<(bool Success, string? Error)> DeleteGatewayAsync(string gatewayId, int enterpriseId)
        {
            if (string.IsNullOrWhiteSpace(gatewayId))
                return (false, "GatewayId é obrigatório");

            Gateway? gateway;
            IList<int> panelIds;
            using (var scope = _provider.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                gateway = await repo.GetGatewayByGatewayIdAsync(gatewayId.Trim());
                if (gateway == null)
                    return (false, "Gateway não encontrado");
                if (gateway.EnterpriseId != enterpriseId)
                    return (false, "Gateway não pertence à empresa selecionada");
                var panels = await repo.GetAllPanels(p => p.GatewayId == gateway.GatewayId);
                panelIds = panels.Select(p => p.Id).ToList();
                repo.Delete(gateway);
                if (!await repo.SaveAll())
                    return (false, "Erro ao remover o gateway");
            }

            _hubBroadcast.RemoveGateway(gateway.GatewayId, panelIds);
            await RemoveGatewayFromStoreAsync(enterpriseId, gateway.GatewayId);
            await _hubBroadcast.BroadcastGatewayRemoved(enterpriseId, gateway.GatewayId);
            return (true, null);
        }

        public async Task AddGatewayToStoreAsync(int enterpriseId, string gatewayId)
        {
            await EnsureStoreInitializedAsync();
            var perEnterprise = _store.GetOrAdd(enterpriseId, _ => new ConcurrentDictionary<string, SystemMessageModel>());
            perEnterprise[gatewayId] = new SystemMessageModel
            {
                GatewayId = gatewayId,
                IsConnected = false,
                Uptime = null,
                IpAddress = null
            };
        }

        public Task RemoveGatewayFromStoreAsync(int enterpriseId, string gatewayId)
        {
            if (_store.TryGetValue(enterpriseId, out var perEnterprise))
                perEnterprise.TryRemove(gatewayId, out _);
            return Task.CompletedTask;
        }

        public async Task AddGatewaySystemInfoAsync(string gatewayId, SystemMessageModel systemData)
        {
            await EnsureStoreInitializedAsync();

            Gateway? gateway;
            using (var scope = _provider.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                gateway = await repo.GetGatewayByGatewayIdAsync(gatewayId);
            }

            if (gateway == null)
            {
                _logger.LogDebug("Gateway not found in database: {GatewayId}", gatewayId);
                return;
            }

            systemData.IsConnected = true;
            systemData.LastActivity = new DateTime(DateTime.UtcNow.Ticks, DateTimeKind.Utc);
            var enterpriseId = gateway.EnterpriseId;
            var perEnterprise = _store.AddOrUpdate(
                enterpriseId,
                _ => new ConcurrentDictionary<string, SystemMessageModel>(),
                (_, d) => d);

            var wasConnected = perEnterprise.TryGetValue(gatewayId, out var previous) && previous.IsConnected;
            if (previous != null)
                systemData.AvailableSensors = previous.AvailableSensors;
            perEnterprise[gatewayId] = systemData;

            if (!wasConnected)
                await _hubBroadcast.BroadcastGatewaySystemInfo(enterpriseId, systemData);
        }

        public async Task UpdateGatewayLastActivityAsync(string gatewayId)
        {
            await EnsureStoreInitializedAsync();

            Gateway? gateway;
            using (var scope = _provider.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                gateway = await repo.GetGatewayByGatewayIdAsync(gatewayId);
            }

            if (gateway == null)
            {
                _logger.LogDebug("Gateway not found in database: {GatewayId}", gatewayId);
                return;
            }

            var now = new DateTime(DateTime.UtcNow.Ticks, DateTimeKind.Utc);
            var enterpriseId = gateway.EnterpriseId;
            var perEnterprise = _store.AddOrUpdate(
                enterpriseId,
                _ => new ConcurrentDictionary<string, SystemMessageModel>(),
                (_, d) => d);
            perEnterprise.AddOrUpdate(
                gatewayId,
                _ => new SystemMessageModel { GatewayId = gatewayId, IsConnected = false, LastActivity = now },
                (_, existing) => { existing.LastActivity = now; return existing; });
        }

        public async Task<IReadOnlyDictionary<string, SystemMessageModel>> GetGatewayInfoByEnterpriseAsync(int enterpriseId)
        {
            await EnsureStoreInitializedAsync();
            if (!_store.TryGetValue(enterpriseId, out var gateways))
                return new Dictionary<string, SystemMessageModel>();
            return new Dictionary<string, SystemMessageModel>(gateways);
        }

        public async Task CheckGatewayHeartbeatTimeoutsAsync(TimeSpan timeout)
        {
            await EnsureStoreInitializedAsync();

            var now = DateTime.UtcNow;
            foreach (var (enterpriseId, perEnterprise) in _store)
            {
                foreach (var systemData in perEnterprise.Values)
                {
                    if (!systemData.IsConnected || systemData.LastActivity == null)
                        continue;

                    if (now - systemData.LastActivity.Value <= timeout)
                        continue;

                    systemData.IsConnected = false;
                    _logger.LogInformation("Gateway {GatewayId} timed out (no message for over {Timeout}), marking offline", systemData.GatewayId, timeout);
                    await _hubBroadcast.BroadcastGatewaySystemInfo(enterpriseId, systemData);
                }
            }
        }
    }
}
