using Data.Sqlite;
using Shared.Models;
using VanadiumAPI.DTO;
using VanadiumAPI.Services.AlarmRegistry;
using VanadiumAPI.SensorDataSaver;

namespace VanadiumAPI.Services
{
    public class PanelService : IPanelService
    {
        private readonly IPanelInfoRepository _repository;
        private readonly IHubBroadcastService _broadcastService;
        private readonly ISensorDataSaver _sensorDataSaver;
        private readonly IAlarmRegistryService _alarmRegistry;
        private readonly ILogger<PanelService> _logger;

        public PanelService(
            IPanelInfoRepository repository,
            IHubBroadcastService broadcastService,
            ISensorDataSaver sensorDataSaver,
            IAlarmRegistryService alarmRegistry,
            ILogger<PanelService> logger)
        {
            _repository = repository;
            _broadcastService = broadcastService;
            _sensorDataSaver = sensorDataSaver;
            _alarmRegistry = alarmRegistry;
            _logger = logger;
        }

        public async Task<(PanelDto? Panel, string? Error)> AddPanelAsync(AddPanelDto dto, int enterpriseId)
        {
            try
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.GatewayId) || string.IsNullOrWhiteSpace(dto.Index))
                    return (null, "GatewayId e Index são obrigatórios");

                var gateway = await _repository.GetGatewayByGatewayIdAsync(dto.GatewayId.Trim());
                if (gateway == null || gateway.EnterpriseId != enterpriseId)
                    return (null, "Gateway não encontrado ou não pertence à empresa");

                var group = await _repository.GetGroupById(dto.GroupId);
                if (group == null || group.EnterpriseId != enterpriseId)
                    return (null, "Grupo não encontrado ou não pertence à empresa");

                var existing = await _repository.GetPanelByGatewayAndIndexAsync(dto.GatewayId.Trim(), dto.Index.Trim());
                if (existing != null)
                    return (null, "Já existe um painel com esse GatewayId e Index");

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
                    Type = dto.Type
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

                return (panelDto, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddPanelAsync failed for enterprise {EnterpriseId}, gateway {GatewayId}, index {Index}",
                    enterpriseId, dto?.GatewayId, dto?.Index);
                return (null, "Erro interno ao adicionar o painel");
            }
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

            if (dto.Name != null) panel.Name = dto.Name.Trim();
            if (dto.Color != null) panel.Color = dto.Color.Trim();
            if (dto.Gain.HasValue) panel.Gain = dto.Gain.Value;
            if (dto.Offset.HasValue) panel.Offset = dto.Offset.Value;
            if (dto.Multiplier.HasValue) panel.Multiplier = dto.Multiplier.Value;
            if (dto.DisplayedType.HasValue) panel.DisplayedType = dto.DisplayedType.Value;

            if (!await _repository.UpdatePanelAsync(panel))
                return (null, "Erro ao atualizar o painel");

            _sensorDataSaver.UpdatePanel(panel);

            if (dto.MaxAlarm.ShouldClearAll)
            {
                var clearErr = await ClearPanelAlarmsOfKindAsync(panel.Id, isGreaterThan: true);
                if (clearErr != null) return (null, clearErr);
            }
            if (dto.MinAlarm.ShouldClearAll)
            {
                var clearErr = await ClearPanelAlarmsOfKindAsync(panel.Id, isGreaterThan: false);
                if (clearErr != null) return (null, clearErr);
            }

            if (dto.MaxAlarm.HasThreshold || dto.MinAlarm.HasThreshold)
            {
                var panelForAlarms = await _repository.GetPanelById(panel.Id);
                if (panelForAlarms == null)
                    return (null, "Painel não encontrado");

                var alarmsToNotifyUpdated = new List<Alarm>();
                var alarmsToNotifyCreated = new List<Alarm>();

                if (dto.MaxAlarm.HasThreshold)
                {
                    var existingHigh = panelForAlarms.Alarms?
                        .Where(a => a.IsGreaterThan)
                        .OrderByDescending(a => a.Id)
                        .FirstOrDefault();
                    if (existingHigh != null)
                    {
                        existingHigh.Threshold = dto.MaxAlarm.Threshold;
                        alarmsToNotifyUpdated.Add(existingHigh);
                    }
                    else
                    {
                        var a = new Alarm
                        {
                            PanelId = panel.Id,
                            Threshold = dto.MaxAlarm.Threshold,
                            IsGreaterThan = true
                        };
                        _repository.Add(a);
                        alarmsToNotifyCreated.Add(a);
                    }
                }

                if (dto.MinAlarm.HasThreshold)
                {
                    var existingLow = panelForAlarms.Alarms?
                        .Where(a => !a.IsGreaterThan)
                        .OrderByDescending(a => a.Id)
                        .FirstOrDefault();
                    if (existingLow != null)
                    {
                        existingLow.Threshold = dto.MinAlarm.Threshold;
                        alarmsToNotifyUpdated.Add(existingLow);
                    }
                    else
                    {
                        var a = new Alarm
                        {
                            PanelId = panel.Id,
                            Threshold = dto.MinAlarm.Threshold,
                            IsGreaterThan = false
                        };
                        _repository.Add(a);
                        alarmsToNotifyCreated.Add(a);
                    }
                }

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
