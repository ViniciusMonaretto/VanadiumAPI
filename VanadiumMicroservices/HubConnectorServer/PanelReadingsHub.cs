using Microsoft.AspNetCore.SignalR;
using Shared.Models;
using API.Services;
using HubConnectorServer.DTO;
using System.IdentityModel.Tokens.Jwt;

namespace API.Hubs
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

        // Connection lifecycle
        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", Context.ConnectionId);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }

        // Authentication
        public async Task<AuthDto?> Login(LoginDto loginDto)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during authentication for email: {Email}", loginDto?.Username);
                throw;
            }
        }

        // Helper method to validate token and extract user info
        // Uses SensorInfoServer API endpoint for token validation (api/auth/validate)
        private async Task<(bool isValid, int userId)> ValidateTokenAndGetUserIdAsync(string? token)
        {
            var (isValid, userId, _) = await ValidateTokenAndGetUserContextAsync(token);
            return (isValid, userId);
        }

        // Validates token and extracts userId and UserType from JWT
        private async Task<(bool isValid, int userId, UserType userType)> ValidateTokenAndGetUserContextAsync(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Empty or null token provided from connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Authentication required");
                return (false, 0, UserType.User);
            }

            var isValid = await _authService.ValidateTokenAsync(token);
            if (!isValid)
            {
                _logger.LogWarning("Invalid or expired token for connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Invalid or expired token");
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
                {
                    userId = parsedUserId;
                }
                var userTypeClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "UserType");
                if (userTypeClaim != null && Enum.TryParse<UserType>(userTypeClaim.Value, out var parsedUserType))
                {
                    userType = parsedUserType;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract user info from token for connection {ConnectionId}", Context.ConnectionId);
            }

            return (true, userId, userType);
        }

        // Panels
        public async Task<IEnumerable<PanelDto>> GetAllPanels(string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid)
            {
                return Enumerable.Empty<PanelDto>();
            }

            try
            {
                var panels = await _sensorInfoService.GetAllPanelsAsync();
                return panels.Select(p => new PanelDto(p));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all panels");
                throw;
            }
        }

        public async Task<PanelDto?> GetPanelById(int id, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid)
            {
                return null;
            }

            try
            {
                var panel = await _sensorInfoService.GetPanelByIdAsync(id);
                return panel != null ? new PanelDto(panel) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching panel {PanelId}", id);
                throw;
            }
        }

        // Alarms
        public async Task<IEnumerable<AlarmDto>> GetAllAlarms(string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid)
            {
                return Enumerable.Empty<AlarmDto>();
            }

            try
            {
                var alarms = await _sensorInfoService.GetAllAlarmsAsync();
                return alarms.Select(a => new AlarmDto(a));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all alarms");
                throw;
            }
        }

        public async Task<AlarmDto?> GetAlarmById(int id, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid)
            {
                return null;
            }

            try
            {
                var alarm = await _sensorInfoService.GetAlarmByIdAsync(id);
                return alarm != null ? new AlarmDto(alarm) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching alarm {AlarmId}", id);
                throw;
            }
        }

        // Alarm Events
        public async Task<IEnumerable<AlarmEventDto>> GetAllAlarmEvents(string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid)
            {
                return Enumerable.Empty<AlarmEventDto>();
            }

            try
            {
                var alarmEvents = await _sensorInfoService.GetAllAlarmEventsAsync();
                if (alarmEvents == null)
                {
                    _logger.LogWarning("GetAllAlarmEventsAsync returned null");
                    return Enumerable.Empty<AlarmEventDto>();
                }

                var result = alarmEvents
                    .Where(ae => ae != null)
                    .Select(ae => new AlarmEventDto(ae))
                    .ToList();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all alarm events: {Message}", ex.Message);
                throw;
            }
        }

        // Groups
        public async Task<Dictionary<string, GroupDto>> SetSelectedEnterprise(SetEnterpriseDto enterprise, string token)
        {
            var (isValid, userId) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid)
            {
                return new Dictionary<string, GroupDto>();
            }

            try
            {
                var groups = await _sensorInfoService.GetAllGroupsAsync(enterprise.EnterpriseId);

                _broadcastService.SetUserPanels(Context.ConnectionId,
                                                userId, groups.SelectMany(g => g.Panels.Select(p => (p.Id, p.GatewayId))).ToList());

                return groups.ToDictionary(g => g.Id.ToString(), g => new GroupDto(g));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all groups");
                throw;
            }
        }

        public async Task<GroupDto?> GetGroupById(int id, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid)
            {
                return null;
            }

            try
            {
                var group = await _sensorInfoService.GetGroupByIdAsync(id);
                return group != null ? new GroupDto(group) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching group {GroupId}", id);
                throw;
            }
        }

        // Panel Readings
        public async Task<IEnumerable<PanelReading>> GetPanelReadings(int panelId, string token, DateTime? startDate = null, DateTime? endDate = null)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid)
            {
                return Enumerable.Empty<PanelReading>();
            }

            try
            {
                var readings = await _panelReadingService.GetPanelReadingsAsync(panelId, startDate, endDate);
                return readings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching panel readings for panel {PanelId}", panelId);
                throw;
            }
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
            if (!isValid)
            {
                return new Dictionary<int, List<PanelReading>>();
            }

            try
            {
                if (request.SensorInfos == null || !request.SensorInfos.Any())
                {
                    _logger.LogWarning("GetMultiplePanelReadings called with null or empty sensorInfos");
                    return new Dictionary<int, List<PanelReading>>();
                }

                var readings = await _panelReadingService.GetMultiplePanelReadingsAsync(request.SensorInfos, request.StartDate, request.EndDate);
                return readings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching multiple panel readings");
                throw;
            }
        }

        // Managed Users
        public async Task<IEnumerable<UserDto>> GetManagedUsers(string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid)
            {
                return Enumerable.Empty<UserDto>();
            }

            try
            {
                var users = await _sensorInfoService.GetManagedUsersAsync(token);
                return users.Select(u => new UserDto(u));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching managed users");
                throw;
            }
        }

        public async Task<UserDto?> AddManagedUser(CreateManagedUserDto dto, string token)
        {
            var (isValid, _, userType) = await ValidateTokenAndGetUserContextAsync(token);
            if (!isValid)
            {
                return null;
            }

            if (userType != UserType.Admin && userType != UserType.Manager)
            {
                _logger.LogWarning("AddManagedUser called by user without manager or admin role");
                await Clients.Caller.SendAsync("Error", "Only managers or admins can add managed users");
                return null;
            }

            try
            {
                var user = await _sensorInfoService.CreateManagedUserAsync(dto, token);
                return user != null ? new UserDto(user) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding managed user");
                throw;
            }
        }

        public async Task<bool> RemoveManagedUser(int userId, string token)
        {
            var (isValid, _) = await ValidateTokenAndGetUserIdAsync(token);
            if (!isValid)
            {
                return false;
            }

            try
            {
                return await _sensorInfoService.DeleteManagedUserAsync(userId, token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing managed user {UserId}", userId);
                throw;
            }
        }
    }
}