using Data.Sqlite;
using Shared.Models;
using VanadiumAPI.DTO;
using VanadiumAPI.SensorDataSaver;

namespace VanadiumAPI.Services
{
    public class PanelService : IPanelService
    {
        private readonly IPanelInfoRepository _repository;
        private readonly IHubBroadcastService _broadcastService;
        private readonly ISensorDataSaver _sensorDataSaver;

        public PanelService(
            IPanelInfoRepository repository,
            IHubBroadcastService broadcastService,
            ISensorDataSaver sensorDataSaver)
        {
            _repository = repository;
            _broadcastService = broadcastService;
            _sensorDataSaver = sensorDataSaver;
        }

        public async Task<(PanelDto? Panel, string? Error)> AddPanelAsync(AddPanelDto dto, int enterpriseId)
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
                return (null, "Erro ao salvar o painel");

            panel = created;
            _sensorDataSaver.AddPanel(panel);
            var panelDto = new PanelDto(panel);
            _broadcastService.AddPanel(enterpriseId, panel.Id, panel.GatewayId);
            await _broadcastService.BroadcastPanelAdded(enterpriseId, panelDto);
            return (panelDto, null);
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

            if (!await _repository.UpdatePanelAsync(panel))
                return (null, "Erro ao atualizar o painel");

            _sensorDataSaver.UpdatePanel(panel);
            await _broadcastService.BroadcastPanelChange(PanelChangeAction.Update, new PanelDto(panel));
            return (new PanelDto(panel), null);
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
    }
}
