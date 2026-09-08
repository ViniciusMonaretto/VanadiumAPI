using Data.Sqlite;
using Shared.Models;
using Shared.Models.Mqtt;
using VanadiumAPI.DTO;
using VanadiumAPI.Services.AlarmRegistry;
using VanadiumAPI.Services.PanelConfiguration;
using VanadiumAPI.SensorDataSaver;

namespace VanadiumAPI.Services
{
    public class PanelService : IPanelService
    {
        private readonly IPanelInfoRepository _repository;
        private readonly IHubBroadcastService _broadcastService;
        private readonly ISensorDataSaver _sensorDataSaver;
        private readonly IAlarmRegistryService _alarmRegistry;
        private readonly IGatewayServerService _gatewayServer;
        private readonly IDeviceInfoStore _deviceInfoStore;
        private readonly IPanelConfigurationService _panelConfig;
        private readonly ILogger<PanelService> _logger;

        public PanelService(
            IPanelInfoRepository repository,
            IHubBroadcastService broadcastService,
            ISensorDataSaver sensorDataSaver,
            IAlarmRegistryService alarmRegistry,
            IGatewayServerService gatewayServer,
            IDeviceInfoStore deviceInfoStore,
            IPanelConfigurationService panelConfig,
            ILogger<PanelService> logger)
        {
            _repository = repository;
            _broadcastService = broadcastService;
            _sensorDataSaver = sensorDataSaver;
            _alarmRegistry = alarmRegistry;
            _gatewayServer = gatewayServer;
            _deviceInfoStore = deviceInfoStore;
            _panelConfig = panelConfig;
            _logger = logger;
        }

        public async Task<(PanelDto? Panel, string? Error)> AddPanelAsync(AddPanelDto dto, int enterpriseId)
        {
            try
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.GatewayId) || string.IsNullOrWhiteSpace(dto.Index))
                    return (null, "GatewayId e Index são obrigatórios");

                // Index is stored as a string but addresses an int sensor_id over MQTT; a non-numeric
                // index could never match telemetry either (SensorDataSaver matches on SensorId.ToString()).
                if (!int.TryParse(dto.Index.Trim(), out var sensorId) || sensorId < 0)
                    return (null, "O índice do sensor deve ser um número inteiro");

                var gateway = await _repository.GetGatewayByGatewayIdAsync(dto.GatewayId.Trim());
                if (gateway == null || gateway.EnterpriseId != enterpriseId)
                    return (null, "Gateway não encontrado ou não pertence à empresa");

                var group = await _repository.GetGroupById(dto.GroupId);
                if (group == null || group.EnterpriseId != enterpriseId)
                    return (null, "Grupo não encontrado ou não pertence à empresa");

                var existing = await _repository.GetPanelByGatewayAndIndexAsync(dto.GatewayId.Trim(), dto.Index.Trim());
                if (existing != null)
                    return (null, "Já existe um painel com esse GatewayId e Index");

                var gateError = await CheckGatewaySupportsSensorAsync(gateway, enterpriseId, sensorId, dto.Type);
                if (gateError != null)
                    return (null, gateError);

                var panel = new Panel
                {
                    Name = dto.Name?.Trim() ?? string.Empty,
                    GatewayId = dto.GatewayId.Trim(),
                    GroupId = dto.GroupId,
                    Index = dto.Index.Trim(),
                    Color = dto.Color?.Trim() ?? string.Empty,
                    Gain = dto.Gain,
                    Offset = dto.Offset,
                    Multiplier = dto.Multiplier,
                    SamplingMs = dto.SamplingMs,
                    Enabled = dto.Enabled,
                    Type = dto.Type,
                    // Cleared once the gateway confirms SET_SENSOR_CONFIG below.
                    Configuring = true
                };
                var created = await _repository.CreatePanelAsync(panel);
                if (created == null)
                {
                    _logger.LogError("CreatePanelAsync returned null for enterprise {EnterpriseId}, gateway {GatewayId}, index {Index}",
                        enterpriseId, dto.GatewayId, dto.Index);
                    return (null, "Erro ao salvar o painel");
                }

                panel = created;
                _sensorDataSaver.AddPanel(panel);
                var panelDto = new PanelDto(panel);
                // Must precede the device round-trip: this populates the panel's subscriber set, which is
                // the only audience of the BroadcastPanelChange that clears the Configuring flag.
                _broadcastService.AddPanel(enterpriseId, panel.Id, panel.GatewayId);
                try
                {
                    await _broadcastService.BroadcastPanelAdded(enterpriseId, panelDto);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "BroadcastPanelAdded failed after panel {PanelId} was created (enterprise {EnterpriseId}); clients may not have received PanelAdded",
                        panel.Id, enterpriseId);
                }

                _panelConfig.StartEnable(panel, enterpriseId, sensorId);

                return (panelDto, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddPanelAsync failed for enterprise {EnterpriseId}, gateway {GatewayId}, index {Index}",
                    enterpriseId, dto?.GatewayId, dto?.Index);
                return (null, "Erro interno ao adicionar o painel");
            }
        }

        /// <summary>
        /// A panel may only be created once GET_SENSORS has succeeded for its gateway. Probes the device
        /// if that has never happened, then verifies the sensor exists and is of the requested type.
        /// Returns null when the panel may be created, or the user-facing reason it may not.
        /// </summary>
        private async Task<string?> CheckGatewaySupportsSensorAsync(Gateway gateway, int enterpriseId, int sensorId, PanelType type)
        {
            if (!gateway.IsSetup)
            {
                var (_, refreshError) = await _gatewayServer.RefreshGatewaySensorsAsync(gateway.GatewayId, enterpriseId);
                if (refreshError != null)
                    return "Não foi possível obter a lista de sensores do gateway. Verifique se ele está online e tente novamente.";
            }

            // The cache holds the full GET_SENSORS result, so it can check the exact sensor_id -> type
            // pairing. AvailableSensors only holds a distinct set of types, with no ids.
            var caps = _deviceInfoStore.GetSensorCapabilities(gateway.GatewayId);
            if (caps != null)
            {
                var match = caps.FirstOrDefault(s => s.SensorId == sensorId);
                if (match == null)
                    return $"O gateway não possui o sensor de índice {sensorId}";
                if (SensorTypeMapper.ToPanelType(match.Type) != type)
                    return $"O sensor de índice {sensorId} do gateway é do tipo '{match.Type}'";
                return null;
            }

            // Cache is cold (the process restarted since the gateway was probed): fall back to the
            // persisted type set.
            if (!gateway.AvailableSensors.Contains(type))
                return "O gateway não suporta sensores desse tipo";

            return null;
        }

        public async Task<(PanelDto? Panel, string? Error)> UpdatePanelAsync(UpdatePanelDto dto, int enterpriseId)
        {
            if (dto == null || dto.Id <= 0)
                return (null, "Id do painel é obrigatório");

            var panel = await _repository.GetPanelById(dto.Id);
            if (panel == null)
                return (null, "Painel não encontrado");

            var gateway = await _repository.GetGatewayByGatewayIdAsync(panel.GatewayId);
            if (gateway == null || gateway.EnterpriseId != enterpriseId)
                return (null, "Painel não pertence à empresa selecionada");

            // An update writes SamplingMs/Enabled, exactly what an outstanding SET_SENSOR_CONFIG is
            // asserting. A stale Configuring panel that is not in flight stays editable.
            if (_panelConfig.IsInFlight(panel.Id))
                return (null, "Painel já está sendo configurado. Aguarde a conclusão.");

            if (dto.Name != null) panel.Name = dto.Name.Trim();
            if (dto.Color != null) panel.Color = dto.Color.Trim();
            if (dto.Gain.HasValue) panel.Gain = dto.Gain.Value;
            if (dto.Offset.HasValue) panel.Offset = dto.Offset.Value;
            if (dto.Multiplier.HasValue) panel.Multiplier = dto.Multiplier.Value;
            if (dto.SamplingMs.HasValue) panel.SamplingMs = dto.SamplingMs.Value;
            if (dto.Enabled.HasValue) panel.Enabled = dto.Enabled.Value;
            if (dto.DisplayedType.HasValue) panel.DisplayedType = dto.DisplayedType.Value;

            if (!await _repository.UpdatePanelAsync(panel))
                return (null, "Erro ao atualizar o painel");

            _sensorDataSaver.UpdatePanel(panel);
            
            var panelForAlarms = await _repository.GetPanelById(panel.Id);
            if (panelForAlarms == null)
                return (null, "Alarmes de painel não encontrado");

            var alarmsToNotifyCreated = new List<Alarm>();
            var alarmsToNotifyUpdated = new List<Alarm>();

            if (!dto.MaxAlarm.HasValue)
            {
                var clearErr = await ClearPanelAlarmsOfKindAsync(panel.Id, isGreaterThan: true);
                if (clearErr != null) return (null, clearErr);
            }
            else
            {
                var existingHigh = panelForAlarms.Alarms?
                    .Where(a => a.IsGreaterThan)
                    .OrderByDescending(a => a.Id)
                    .FirstOrDefault();
                if (existingHigh != null)
                {

                    if (existingHigh.Threshold != dto.MaxAlarm.Value || existingHigh.Severity != dto.AlarmSeverity)
                    {
                        existingHigh.Threshold = dto.MaxAlarm.Value;
                        if (dto.AlarmSeverity.HasValue)
                            existingHigh.Severity = dto.AlarmSeverity.Value;
                        alarmsToNotifyUpdated.Add(existingHigh);
                    }
                }
                else
                {
                    var a = new Alarm
                    {
                        PanelId = panel.Id,
                        Threshold = dto.MaxAlarm.Value,
                        IsGreaterThan = true,
                        Severity = dto.AlarmSeverity ?? AlarmSeverity.Warning
                    };
                    _repository.Add(a);
                    alarmsToNotifyCreated.Add(a);
                }
            }

            if (!dto.MinAlarm.HasValue)
            {
                var clearErr = await ClearPanelAlarmsOfKindAsync(panel.Id, isGreaterThan: false);
                if (clearErr != null) return (null, clearErr);
            }
            else
            {
                var existingLow = panelForAlarms.Alarms?
                    .Where(a => !a.IsGreaterThan)
                    .OrderByDescending(a => a.Id)
                    .FirstOrDefault();
                if (existingLow != null)
                {
                    if (existingLow.Threshold != dto.MinAlarm.Value || existingLow.Severity != dto.AlarmSeverity)
                    {
                        existingLow.Threshold = dto.MinAlarm.Value;
                        if (dto.AlarmSeverity.HasValue)
                            existingLow.Severity = dto.AlarmSeverity.Value;
                        alarmsToNotifyUpdated.Add(existingLow);
                    }
                }
                else
                {
                    var a = new Alarm
                    {
                        PanelId = panel.Id,
                        Threshold = dto.MinAlarm.Value,
                        IsGreaterThan = false,
                        Severity = dto.AlarmSeverity ?? AlarmSeverity.Warning
                    };
                    _repository.Add(a);
                    alarmsToNotifyCreated.Add(a);
                }
            }

            if (alarmsToNotifyUpdated.Count > 0 || alarmsToNotifyCreated.Count > 0)
            {
                if (!await _repository.SaveAll())
                    return (null, "Erro ao salvar alarmes do painel");
                foreach (var a in alarmsToNotifyUpdated)
                    _alarmRegistry.NotifyAlarmUpdated(a);
                foreach (var a in alarmsToNotifyCreated)
                    _alarmRegistry.NotifyAlarmCreated(a);
            }

            var refreshed = await _repository.GetPanelById(panel.Id);
            if (refreshed == null)
                return (null, "Painel não encontrado após atualização");
            var panelDto = new PanelDto(refreshed);
            await _broadcastService.BroadcastPanelChange(PanelChangeAction.Update, panelDto);
            return (panelDto, null);
        }

        public async Task<(bool Success, string? Error)> DeletePanelAsync(int panelId, int enterpriseId)
        {
            var panel = await _repository.GetPanelById(panelId);
            if (panel == null)
                return (false, "Painel não encontrado");

            var gateway = await _repository.GetGatewayByGatewayIdAsync(panel.GatewayId);
            if (gateway == null || gateway.EnterpriseId != enterpriseId)
                return (false, "Painel não pertence à empresa selecionada");

            if (_panelConfig.IsInFlight(panelId))
                return (false, "Painel já está sendo configurado. Aguarde a conclusão.");

            // Not addressable on the device, so there is nothing to disable there.
            if (!int.TryParse(panel.Index, out var sensorId) || sensorId < 0)
                return await PurgePanelLocallyAsync(panel, enterpriseId);

            // Configuring means the device never confirmed this panel, so its sensor was never enabled:
            // there is nothing to undo. This is also the escape hatch for a panel whose enable failed.
            if (panel.Configuring)
                return await PurgePanelLocallyAsync(panel, enterpriseId);

            // A gateway the heartbeat says is offline cannot answer; the round-trip would only time out.
            var gatewayInfo = await _gatewayServer.GetGatewayInfoByEnterpriseAsync(enterpriseId);
            if (!gatewayInfo.TryGetValue(panel.GatewayId, out var info) || !info.IsConnected)
            {
                _logger.LogWarning("Gateway {GatewayId} is offline; deleting panel {PanelId} locally without disabling its sensor",
                    panel.GatewayId, panelId);
                return await PurgePanelLocallyAsync(panel, enterpriseId);
            }

            panel.Configuring = true;
            if (!await _repository.UpdatePanelAsync(panel))
                return (false, "Erro ao atualizar o painel");

            // The row stays until the device confirms, so its telemetry is still legitimately recorded.
            await _broadcastService.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto(panel));
            _panelConfig.StartDisableAndDelete(panel, enterpriseId, sensorId);
            return (true, null);
        }

        /// <summary>Removes the panel from the system without touching the device. Used when the device
        /// has nothing to disable (the panel was never confirmed) or cannot be reached.</summary>
        private async Task<(bool Success, string? Error)> PurgePanelLocallyAsync(Panel panel, int enterpriseId)
        {
            var panelId = panel.Id;
            var gatewayId = panel.GatewayId;
            var index = panel.Index;
            if (!await _repository.DeletePanelAsync(panel))
                return (false, "Erro ao remover o painel");

            _broadcastService.RemovePanel(panelId, gatewayId);
            _sensorDataSaver.RemovePanel(panelId, gatewayId, index);
            await _broadcastService.BroadcastPanelRemoved(enterpriseId, panelId);
            return (true, null);
        }

        private async Task<string?> ClearPanelAlarmsOfKindAsync(int panelId, bool isGreaterThan)
        {
            var fresh = await _repository.GetPanelById(panelId);
            if (fresh?.Alarms == null) return null;
            var toRemove = fresh.Alarms.Where(a => a.IsGreaterThan == isGreaterThan).ToList();
            if (toRemove.Count == 0) return null;
            foreach (var alarm in toRemove)
                _repository.Delete(alarm);
            if (!await _repository.SaveAll())
                return "Erro ao remover alarmes do painel";
            foreach (var alarm in toRemove)
                _alarmRegistry.NotifyAlarmDeleted(alarm.Id);
            return null;
        }
    }
}
