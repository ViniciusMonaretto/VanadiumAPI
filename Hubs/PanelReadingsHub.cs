using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.SignalR;
using Shared.Models;
using VanadiumAPI.DTO;
using VanadiumAPI.DTOs;
using VanadiumAPI.Services;
using LoginDto = VanadiumAPI.DTOs.LoginDto;

namespace VanadiumAPI.Hubs
{
    public class PanelReadingsHub : Hub
    {
        private readonly ISensorInfoService _sensorInfoService;
        private readonly IPanelReadingService _panelReadingService;
        private readonly IAuthService _authService;
        private readonly IPanelBroadcastService _broadcastService;
        private readonly ILogger<PanelReadingsHub> _logger;

        public PanelReadingsHub(
            ISensorInfoService sensorInfoService,
            IPanelReadingService panelReadingService,
            IAuthService authService,
            IPanelBroadcastService broadcastService,
            ILogger<PanelReadingsHub> logger)
        {
            _sensorInfoService = sensorInfoService;
            _panelReadingService = panelReadingService;
            _authService = authService;
            _broadcastService = broadcastService;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
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
            _broadcastService.SetUserPanels(Context.ConnectionId, userId, groups.SelectMany(g => g.Panels.Select(p => (p.Id, p.GatewayId))).ToList());
            return groups.ToDictionary(g => g.Id.ToString(), g => new GroupDto(g));
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
    }
}
