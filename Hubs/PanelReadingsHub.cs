using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.SignalR;
using Shared.Models;
using VanadiumAPI.DTO;
using VanadiumAPI.DTOs;
using VanadiumAPI.Services;
using VanadiumAPI.SensorDataSaver;
using LoginDto = VanadiumAPI.DTOs.LoginDto;

namespace VanadiumAPI.Hubs
{
    public class PanelReadingsHub : Hub
    {
        private readonly ISensorInfoService _sensorInfoService;
        private readonly IPanelReadingService _panelReadingService;
        private readonly IAuthService _authService;
        private readonly IHubBroadcastService _broadcastService;
        private readonly IGatewayServerService _gatewayServer;
        private readonly IPanelService _panelService;
        private readonly ISensorDataSaver _sensorDataSaver;
        private readonly ILogger<PanelReadingsHub> _logger;

        public PanelReadingsHub(
            ISensorInfoService sensorInfoService,
            IPanelReadingService panelReadingService,
            IAuthService authService,
            IHubBroadcastService broadcastService,
            IGatewayServerService gatewayServer,
            IPanelService panelService,
            ISensorDataSaver sensorDataSaver,
            ILogger<PanelReadingsHub> logger)
        {
            _sensorInfoService = sensorInfoService;
            _panelReadingService = panelReadingService;
            _authService = authService;
            _broadcastService = broadcastService;
            _gatewayServer = gatewayServer;
            _panelService = panelService;
            _sensorDataSaver = sensorDataSaver;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _broadcastService.RemoveConnection(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        public async Task<AuthDto?> Login(LoginDto loginDto)
        {
            if (loginDto == null || string.IsNullOrEmpty(loginDto.Username) || string.IsNullOrEmpty(loginDto.Password))
            {
                _logger.LogWarning("Authentication attempt with null or empty credentials");
                return null;
            }
            var result = await _authService.LoginAsync(loginDto);
            if (result == null)
            {
                _logger.LogWarning("Authentication failed for email: {Email}", loginDto.Username);
                return null;
            }
            _logger.LogInformation("Authentication successful for email: {Email}", loginDto.Username);
            return new AuthDto(result);
        }

        private async Task<(bool isValid, int userId)> ValidateTokenAndGetUserIdAsync(string? token)
        {
            var (isValid, userId, _) = await ValidateTokenAndGetUserContextAsync(token);
            return (isValid, userId);
        }

        private async Task<(bool isValid, int userId, UserType userType)> ValidateTokenAndGetUserContextAsync(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                await Clients.Caller.SendAsync("Error", "Autenticação necessária");
                return (false, 0, UserType.User);
            }
            var isValid = await _authService.ValidateTokenAsync(token);
            if (!isValid)
            {
                await Clients.Caller.SendAsync("Error", "Token inválido ou expirado");
                return (false, 0, UserType.User);
            }
            int userId = 0;
            var userType = UserType.User;
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jsonToken = handler.ReadJwtToken(token);
                var userIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier);
                if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var parsedUserId))
                    userId = parsedUserId;
                var userTypeClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "UserType");
                if (userTypeClaim != null && Enum.TryParse<UserType>(userTypeClaim.Value, out var parsedUserType))
                    userType = parsedUserType;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract user info from token");
            }
            return (true, userId, userType);
        }

        public async Task<IEnumerable<PanelDto>> GetAllPanels(string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return Enumerable.Empty<PanelDto>();
            var panels = await _sensorInfoService.GetAllPanelsAsync();
            return panels.Select(p => new PanelDto(p));
        }

        public async Task<PanelDto?> GetPanelById(int id, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return null;
            var panel = await _sensorInfoService.GetPanelByIdAsync(id);
            return panel != null ? new PanelDto(panel) : null;
        }

        public async Task<IEnumerable<AlarmDto>> GetAllAlarms(string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return Enumerable.Empty<AlarmDto>();
            var alarms = await _sensorInfoService.GetAllAlarmsAsync();
            return alarms.Select(a => new AlarmDto(a));
        }

        public async Task<AlarmDto?> GetAlarmById(int id, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return null;
            var alarm = await _sensorInfoService.GetAlarmByIdAsync(id);
            return alarm != null ? new AlarmDto(alarm) : null;
        }

        public async Task<IEnumerable<AlarmEventDto>> GetAllAlarmEvents(string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return Enumerable.Empty<AlarmEventDto>();
            var alarmEvents = await _sensorInfoService.GetAllAlarmEventsAsync();
            return alarmEvents?.Where(ae => ae != null).Select(ae => new AlarmEventDto(ae!)).ToList() ?? Enumerable.Empty<AlarmEventDto>();
        }

        public async Task<Dictionary<string, GroupDto>> SetSelectedEnterprise(SetEnterpriseDto enterprise, string token)
        {
            var (isValid, userId) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return new Dictionary<string, GroupDto>();
            var groups = await _sensorInfoService.GetAllGroupsAsync(enterprise.EnterpriseId);
            _broadcastService.SetUserPanels(Context.ConnectionId, userId, enterprise.EnterpriseId, groups.SelectMany(g => g.Panels.Select(p => (p.Id, p.GatewayId))).ToList());
            var panelIds = groups.SelectMany(g => g.Panels ?? Array.Empty<Panel>()).Select(p => p.Id).ToArray();
            var flowByPanel = panelIds.Length == 0
                ? new Dictionary<int, List<FlowConsumption>>()
                : await _panelReadingService.GetFlowConsumptionsOfPanelsAsync(panelIds);
            return groups.ToDictionary(
                g => g.Id.ToString(),
                g => new GroupDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    Panels = (g.Panels ?? Array.Empty<Panel>()).Select(p => BuildPanelDtoForEnterprise(p, flowByPanel)).ToList()
                });
        }

        private PanelDto BuildPanelDtoForEnterprise(Panel panel, IReadOnlyDictionary<int, List<FlowConsumption>> flowByPanel)
        {
            var dto = new PanelDto(panel);
            var last = _sensorDataSaver.GetLastPanelReading(panel.Id);
            if (last != null)
            {
                dto.LastReadingTime = last.ReadingTime;
                dto.Value = last.Value;
                dto.Active = last.Active;
            }
            if (panel.Type == PanelType.Flow
                && flowByPanel.TryGetValue(panel.Id, out var fcList)
                && fcList is { Count: > 0 })
            {
                dto.FlowConsumption = FlowConsumptionDto.FromModel(fcList[0]);;
            }
                
            return dto;
        }

        public async Task<IReadOnlyDictionary<string, SystemMessageModel>> GetGatewayInfo(string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return new Dictionary<string, SystemMessageModel>();
            var enterpriseId = _broadcastService.GetConnectionEnterpriseId(Context.ConnectionId);
            if (enterpriseId == null) return new Dictionary<string, SystemMessageModel>();
            return await _gatewayServer.GetGatewayInfoByEnterpriseAsync(enterpriseId.Value);
        }

        public async Task<GroupDto?> GetGroupById(int id, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return null;
            var group = await _sensorInfoService.GetGroupByIdAsync(id);
            return group != null ? new GroupDto(group) : null;
        }

        public async Task<IEnumerable<PanelReading>> GetPanelReadings(int panelId, string token, DateTime? startDate = null, DateTime? endDate = null)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return Enumerable.Empty<PanelReading>();
            return await _panelReadingService.GetPanelReadingsAsync(panelId, startDate, endDate);
        }

        public async Task<FlowConsumption> GetPanelFlowConsumption(int panelId, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return null;

            var flowConsumptions = await _panelReadingService.GetFlowConsumptionsOfPanelsAsync(new[] { panelId });
            if (!flowConsumptions.TryGetValue(panelId, out var list) || list == null || list.Count == 0)
                return null;

            return list[0];
        }

        public class PanelReadingsRequest
        {
            public int[] SensorInfos { get; set; } = Array.Empty<int>();
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        }

        public async Task<Dictionary<int, List<PanelReading>>> GetMultiplePanelReadings(PanelReadingsRequest request, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return new Dictionary<int, List<PanelReading>>();
            if (request.SensorInfos == null || !request.SensorInfos.Any())
                return new Dictionary<int, List<PanelReading>>();
            return await _panelReadingService.GetMultiplePanelReadingsAsync(request.SensorInfos, request.StartDate, request.EndDate);
        }

        public async Task<IEnumerable<UserDto>> GetManagedUsers(string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return Enumerable.Empty<UserDto>();
            var users = await _sensorInfoService.GetManagedUsersAsync(token);
            return users.Select(u => new UserDto(u));
        }

        public async Task<UserDto?> AddManagedUser(CreateManagedUserDto dto, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return null;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem adicionar usuários gerenciados");
                return null;
            }
            var user = await _sensorInfoService.CreateManagedUserAsync(dto, token);
            return user != null ? new UserDto(user) : null;
        }

        public async Task<bool> RemoveManagedUser(int userId, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return false;
            return await _sensorInfoService.DeleteManagedUserAsync(userId, token);
        }

        public async Task<IEnumerable<EnterpriseDto>> GetUserEnterprises(int userId, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid) return Enumerable.Empty<EnterpriseDto>();
            var enterprises = await _sensorInfoService.GetUserEnterprisesAsync(userId, token);
            return enterprises.Select(e => new EnterpriseDto(e));
        }

        public async Task<bool> AddUserToEnterprise(int userId, int enterpriseId, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return false;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem adicionar um usuário a uma empresa");
                return false;
            }
            try
            {
                return await _sensorInfoService.AddUserToEnterpriseAsync(userId, enterpriseId, token);
            }
            catch (Exception ex)
            {
                var message = ex is InvalidOperationException ? ex.Message : "Ocorreu um erro. Tente novamente.";
                await Clients.Caller.SendAsync("Error", message);
                return false;
            }
        }

        public async Task<bool> RemoveUserFromEnterprise(int userId, int enterpriseId, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return false;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem remover um usuário de uma empresa");
                return false;
            }
            return await _sensorInfoService.RemoveUserFromEnterpriseAsync(userId, enterpriseId, token);
        }

        public async Task<string?> AddGateway(AddGatewayDto dto, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return null;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem adicionar um gateway");
                return null;
            }
            var enterpriseId = _broadcastService.GetConnectionEnterpriseId(Context.ConnectionId);
            if (enterpriseId == null)
            {
                await Clients.Caller.SendAsync("Error", "Selecione uma empresa antes de adicionar um gateway");
                return null;
            }
            var (gatewayId, error) = await _gatewayServer.AddGatewayAsync(dto, enterpriseId.Value);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
                return null;
            }
            return gatewayId;
        }

        public async Task<bool> DeleteGateway(string gatewayId, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return false;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem remover um gateway");
                return false;
            }
            var enterpriseId = _broadcastService.GetConnectionEnterpriseId(Context.ConnectionId);
            if (enterpriseId == null)
            {
                await Clients.Caller.SendAsync("Error", "Selecione uma empresa antes de remover um gateway");
                return false;
            }
            var (success, error) = await _gatewayServer.DeleteGatewayAsync(gatewayId, enterpriseId.Value);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
                return false;
            }
            return success;
        }

        public async Task<PanelDto?> AddPanel(AddPanelDto dto, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return null;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem adicionar um painel");
                return null;
            }
            var enterpriseId = _broadcastService.GetConnectionEnterpriseId(Context.ConnectionId);
            if (enterpriseId == null)
            {
                await Clients.Caller.SendAsync("Error", "Selecione uma empresa antes de adicionar um painel");
                return null;
            }
            var (panel, error) = await _panelService.AddPanelAsync(dto, enterpriseId.Value);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
                return null;
            }
            return panel;
        }

        public async Task<PanelDto?> UpdatePanel(UpdatePanelDto dto, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return null;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem modificar um painel");
                return null;
            }
            var enterpriseId = _broadcastService.GetConnectionEnterpriseId(Context.ConnectionId);
            if (enterpriseId == null)
            {
                await Clients.Caller.SendAsync("Error", "Selecione uma empresa antes de modificar um painel");
                return null;
            }
            var (panel, error) = await _panelService.UpdatePanelAsync(dto, enterpriseId.Value);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
                return null;
            }
            return panel;
        }

        public async Task<bool> DeletePanel(int panelId, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return false;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem remover um painel");
                return false;
            }
            var enterpriseId = _broadcastService.GetConnectionEnterpriseId(Context.ConnectionId);
            if (enterpriseId == null)
            {
                await Clients.Caller.SendAsync("Error", "Selecione uma empresa antes de remover um painel");
                return false;
            }
            var (success, error) = await _panelService.DeletePanelAsync(panelId, enterpriseId.Value);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
                return false;
            }
            return success;
        }

        /// <summary>Create a group in the enterprise from the connection (after SetSelectedEnterprise). Notifies all clients on that enterprise via GroupCreated.</summary>
        public async Task<GroupDto?> AddGroup(CreateGroupDto dto, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return null;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem criar grupos");
                return null;
            }
            var enterpriseId = _broadcastService.GetConnectionEnterpriseId(Context.ConnectionId);
            if (enterpriseId == null)
            {
                await Clients.Caller.SendAsync("Error", "Selecione uma empresa antes de criar um grupo");
                return null;
            }
            var group = await _sensorInfoService.CreateGroupAsync(dto?.Name ?? string.Empty, enterpriseId.Value);
            if (group == null)
            {
                await Clients.Caller.SendAsync("Error", "Não foi possível criar o grupo (nome inválido ou empresa inexistente)");
                return null;
            }
            var groupDto = new GroupDto(group);
            await _broadcastService.BroadcastGroupCreated(enterpriseId.Value, groupDto);
            return groupDto;
        }

        public async Task<GroupDto?> UpdateGroup(UpdateGroupDto dto, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return null;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem atualizar grupos");
                return null;
            }
            var enterpriseId = _broadcastService.GetConnectionEnterpriseId(Context.ConnectionId);
            if (enterpriseId == null)
            {
                await Clients.Caller.SendAsync("Error", "Selecione uma empresa antes de atualizar um grupo");
                return null;
            }
            var (group, error) = await _sensorInfoService.UpdateGroupAsync(dto.Id, dto.Name ?? string.Empty, enterpriseId.Value);
            if (error != null || group == null)
            {
                await Clients.Caller.SendAsync("Error", error ?? "Falha ao atualizar grupo");
                return null;
            }
            var groupDto = new GroupDto(group);
            await _broadcastService.BroadcastGroupUpdated(enterpriseId.Value, groupDto);
            return groupDto;
        }

        public async Task<bool> RemoveGroup(int groupId, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid) return false;
            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                await Clients.Caller.SendAsync("Error", "Apenas gerentes ou administradores podem remover grupos");
                return false;
            }
            var enterpriseId = _broadcastService.GetConnectionEnterpriseId(Context.ConnectionId);
            if (enterpriseId == null)
            {
                await Clients.Caller.SendAsync("Error", "Selecione uma empresa antes de remover um grupo");
                return false;
            }
            var group = await _sensorInfoService.GetGroupByIdAsync(groupId);
            if (group == null || group.EnterpriseId != enterpriseId.Value)
            {
                await Clients.Caller.SendAsync("Error", "Grupo não encontrado nesta empresa");
                return false;
            }
            foreach (var p in group.Panels ?? Array.Empty<Panel>())
                _broadcastService.RemovePanel(p.Id, p.GatewayId);
            var (success, error) = await _sensorInfoService.DeleteGroupAsync(groupId, enterpriseId.Value);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
                return false;
            }
            await _broadcastService.BroadcastGroupRemoved(enterpriseId.Value, groupId);
            return success;
        }
    }
}
