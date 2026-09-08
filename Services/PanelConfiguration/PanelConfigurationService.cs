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
