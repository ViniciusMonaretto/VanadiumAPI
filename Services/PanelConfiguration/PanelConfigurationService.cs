using System.Collections.Concurrent;
using Data.Sqlite;
using Shared.Models;
using Shared.Models.Mqtt;
using VanadiumAPI.DTO;
using VanadiumAPI.SensorDataSaver;
using VanadiumAPI.Services.DeviceCommands;

namespace VanadiumAPI.Services.PanelConfiguration
{
    /// <summary>
    /// Singleton: the device round-trip outlives the request scope that started it, so every database
    /// write below re-loads the panel inside a fresh scope. The Panel passed in is a detached carrier only.
    /// </summary>
    public class PanelConfigurationService : IPanelConfigurationService
    {
        private readonly IServiceProvider _provider;
        private readonly IDeviceCommandService _commands;
        private readonly IHubBroadcastService _broadcast;
        private readonly ISensorDataSaver _sensorDataSaver;
        private readonly ILogger<PanelConfigurationService> _logger;

        private readonly ConcurrentDictionary<int, byte> _inFlight = new();

        public PanelConfigurationService(
            IServiceProvider provider,
            IDeviceCommandService commands,
            IHubBroadcastService broadcast,
            ISensorDataSaver sensorDataSaver,
            ILogger<PanelConfigurationService> logger)
        {
            _provider = provider;
            _commands = commands;
            _broadcast = broadcast;
            _sensorDataSaver = sensorDataSaver;
            _logger = logger;
        }

        public bool IsInFlight(int panelId) => _inFlight.ContainsKey(panelId);

        public void StartEnable(Panel panel, int enterpriseId, int sensorId)
        {
            if (!_inFlight.TryAdd(panel.Id, 0)) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunEnableAsync(panel, enterpriseId, sensorId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Enable task crashed for panel {PanelId}", panel.Id);
                }
                finally
                {
                    _inFlight.TryRemove(panel.Id, out _);
                }
            });
        }

        public void StartDisableAndDelete(Panel panel, int enterpriseId, int sensorId)
        {
            if (!_inFlight.TryAdd(panel.Id, 0)) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunDisableAndDeleteAsync(panel, enterpriseId, sensorId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Disable/delete task crashed for panel {PanelId}", panel.Id);
                }
                finally
                {
                    _inFlight.TryRemove(panel.Id, out _);
                }
            });
        }

        private async Task RunEnableAsync(Panel panel, int enterpriseId, int sensorId)
        {
            var (ok, error) = await _commands.SetSensorConfigAsync(panel.GatewayId, BuildParams(sensorId, panel, enabled: true));
            if (!ok)
            {
                // Deliberately no rollback and no flag clear: the panel stays Configuring so the user
                // can see it was never accepted by the device, and so deleting it purges locally.
                _logger.LogWarning("Enabling sensor {SensorId} on gateway {GatewayId} for panel {PanelId} failed: {Error}",
                    sensorId, panel.GatewayId, panel.Id, error);
                await _broadcast.BroadcastError(enterpriseId,
                    $"Falha ao configurar o sensor '{panel.Name}' no gateway {panel.GatewayId}: {error}");
                return;
            }

            using var scope = _provider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
            var fresh = await repo.GetPanelById(panel.Id);
            if (fresh == null)
            {
                _logger.LogDebug("Panel {PanelId} no longer exists; discarding SET_SENSOR_CONFIG confirmation", panel.Id);
                return;
            }

            fresh.Configuring = false;
            if (!await repo.UpdatePanelAsync(fresh))
            {
                _logger.LogError("Failed to clear Configuring on panel {PanelId} after the device confirmed", panel.Id);
                return;
            }

            _sensorDataSaver.UpdatePanel(fresh);
            await _broadcast.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto(fresh));
        }

        private async Task RunDisableAndDeleteAsync(Panel panel, int enterpriseId, int sensorId)
        {
            var (ok, error) = await _commands.SetSensorConfigAsync(panel.GatewayId, BuildParams(sensorId, panel, enabled: false));
            if (!ok)
            {
                // The row keeps Configuring = true, so a second delete attempt purges it locally.
                _logger.LogWarning("Disabling sensor {SensorId} on gateway {GatewayId} for panel {PanelId} failed: {Error}",
                    sensorId, panel.GatewayId, panel.Id, error);
                await _broadcast.BroadcastError(enterpriseId,
                    $"Falha ao desativar o sensor '{panel.Name}' no gateway {panel.GatewayId}: {error}. " +
                    "Tente remover novamente para excluir apenas do sistema.");
                return;
            }

            using (var scope = _provider.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                var fresh = await repo.GetPanelById(panel.Id);
                if (fresh == null)
                {
                    _logger.LogDebug("Panel {PanelId} already removed; nothing to delete", panel.Id);
                    return;
                }
                if (!await repo.DeletePanelAsync(fresh))
                {
                    _logger.LogError("Failed to delete panel {PanelId} after the device confirmed the disable", panel.Id);
                    await _broadcast.BroadcastError(enterpriseId, $"Erro ao remover o painel '{panel.Name}'");
                    return;
                }
            }

            _broadcast.RemovePanel(panel.Id, panel.GatewayId);
            _sensorDataSaver.RemovePanel(panel.Id, panel.GatewayId, panel.Index);
            await _broadcast.BroadcastPanelRemoved(enterpriseId, panel.Id);
        }

        public void ResumePendingConfigurations(string gatewayId, int enterpriseId)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunResumePendingAsync(gatewayId, enterpriseId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Resume-pending-configuration task crashed for gateway {GatewayId}", gatewayId);
                }
            });
        }

        private async Task RunResumePendingAsync(string gatewayId, int enterpriseId)
        {
            List<Panel> pending;
            using (var scope = _provider.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                var panels = await repo.GetAllPanels(p => p.GatewayId == gatewayId && p.Configuring);
                pending = panels
                    .Where(p => int.TryParse(p.Index, out var sid) && sid >= 0)
                    .ToList();
            }
            if (pending.Count == 0) return;

            // Claim every panel up front so a concurrent Start*/heartbeat call can't also pick it up.
            var claimed = pending.Where(p => _inFlight.TryAdd(p.Id, 0)).ToList();
            if (claimed.Count == 0) return;

            try
            {
                var sensors = claimed
                    .Select(p => BuildParams(int.Parse(p.Index), p, enabled: true))
                    .ToList();
                var (ok, errors, error) = await _commands.SetSensorConfigBulkAsync(gatewayId, sensors);

                if (ok)
                {
                    await FinishConfiguredPanelsAsync(claimed, gatewayId);
                    return;
                }

                if (errors != null && errors.Count > 0)
                {
                    var failedSensorIds = errors.Select(e => e.SensorId).ToHashSet();
                    var succeeded = claimed.Where(p => !failedSensorIds.Contains(int.Parse(p.Index))).ToList();
                    await FinishConfiguredPanelsAsync(succeeded, gatewayId);

                    // Panels whose sensor_id is in failedSensorIds stay Configuring for the next heartbeat retry.
                    _logger.LogWarning("Gateway {GatewayId} rejected {Count} sensor(s) while resuming pending configuration",
                        gatewayId, errors.Count);
                    await _broadcast.BroadcastError(enterpriseId,
                        $"Falha ao concluir a configuração de {errors.Count} sensor(es) no gateway {gatewayId}");
                    return;
                }

                _logger.LogWarning("SET_SENSOR_CONFIG_BULK failed while resuming pending configuration for gateway {GatewayId}: {Error}",
                    gatewayId, error);
                await _broadcast.BroadcastError(enterpriseId,
                    $"Falha ao concluir a configuração pendente do gateway {gatewayId}: {error}");
            }
            finally
            {
                foreach (var p in claimed)
                    _inFlight.TryRemove(p.Id, out _);
            }
        }

        /// <summary>Clears Configuring on each panel the device confirmed and broadcasts the change.</summary>
        private async Task FinishConfiguredPanelsAsync(IReadOnlyCollection<Panel> panels, string gatewayId)
        {
            if (panels.Count == 0) return;

            using var scope = _provider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
            foreach (var panel in panels)
            {
                var fresh = await repo.GetPanelById(panel.Id);
                if (fresh == null)
                {
                    _logger.LogDebug("Panel {PanelId} no longer exists; discarding resumed SET_SENSOR_CONFIG confirmation", panel.Id);
                    continue;
                }

                fresh.Configuring = false;
                if (!await repo.UpdatePanelAsync(fresh))
                {
                    _logger.LogError("Failed to clear Configuring on panel {PanelId} after gateway {GatewayId} confirmed resumed config",
                        panel.Id, gatewayId);
                    continue;
                }

                _sensorDataSaver.UpdatePanel(fresh);
                await _broadcast.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto(fresh));
            }
        }

        private static SetSensorConfigParams BuildParams(int sensorId, Panel panel, bool enabled) => new()
        {
            SensorId = sensorId,
            Offset = panel.Offset,
            Gain = panel.Gain,
            // Guards rows written before the SamplingMs column shim, which defaulted them to 0.
            SamplingMs = panel.SamplingMs > 0 ? panel.SamplingMs : 1000,
            Enabled = enabled
        };
    }
}
