using System.Security.Claims;
using Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using VanadiumAPI.DTO;

namespace VanadiumAPI.Services
{
    public class LocalSensorInfoService : ISensorInfoService
    {
        private readonly IPanelInfoRepository _panelRepo;
        private readonly SqliteDataContext _context;
        private readonly IAuthService _authService;
        private readonly ILogger<LocalSensorInfoService> _logger;

        public LocalSensorInfoService(
            IPanelInfoRepository panelRepo,
            SqliteDataContext context,
            IAuthService authService,
            ILogger<LocalSensorInfoService> logger)
        {
            _panelRepo = panelRepo;
            _context = context;
            _authService = authService;
            _logger = logger;
        }

        public Task<IEnumerable<Panel>> GetAllPanelsAsync() => _panelRepo.GetAllPanels();
        public Task<Panel?> GetPanelByIdAsync(int id) => _panelRepo.GetPanelById(id);
        public Task<IEnumerable<Alarm>> GetAllAlarmsAsync() => _panelRepo.GetAllAlarms();
        public Task<Alarm?> GetAlarmByIdAsync(int id) => _panelRepo.GetAlarmById(id);
        public Task<IEnumerable<AlarmEvent>> GetAllAlarmEventsAsync() => _panelRepo.GetAllAlarmEvents();

        public async Task<IEnumerable<Group>> GetAllGroupsAsync(int? enterpriseId)
        {
            if (enterpriseId.HasValue)
            {
                var list = await _panelRepo.GetEnterpriseGroups(enterpriseId.Value);
                return list;
            }
            return await _panelRepo.GetAllGroups();
        }

        public Task<Group?> GetGroupByIdAsync(int id) => _panelRepo.GetGroupById(id);

        public async Task<IEnumerable<UserInfo>> GetManagedUsersAsync(string token)
        {
            var userId = GetUserIdFromToken(token);
            if (userId == null) return Enumerable.Empty<UserInfo>();

            var users = await _context.Users
                .Where(u => u.ManagerId == userId)
                .ToListAsync();
            foreach (var u in users) u.PasswordHash = string.Empty;
            return users;
        }

        public async Task<UserInfo?> CreateManagedUserAsync(CreateManagedUserDto dto, string token)
        {
            var managerId = GetUserIdFromToken(token);
            if (managerId == null) return null;

            var existing = await _authService.GetUserByEmailAsync(dto.Email);
            if (existing != null) return null;

            var newUser = new UserInfo
            {
                Name = dto.Name,
                UserName = dto.Username,
                Email = dto.Email,
                Company = dto.Company,
                PasswordHash = AuthService.HashPassword(dto.Password),
                UserType = dto.UserType,
                ManagerId = managerId
            };
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();
            newUser.PasswordHash = string.Empty;
            return newUser;
        }

        public async Task<bool> DeleteManagedUserAsync(int userId, string token)
        {
            var currentUserId = GetUserIdFromToken(token);
            var userType = GetUserTypeFromToken(token);
            if (currentUserId == null) return false;

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return false;

            if (userType != UserType.Admin && user.ManagerId != currentUserId)
                return false;

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<Enterprise>> GetUserEnterprisesAsync(int userId, string token)
        {
            var currentUserId = GetUserIdFromToken(token);
            var userType = GetUserTypeFromToken(token);
            if (currentUserId == null) return Enumerable.Empty<Enterprise>();

            var user = await _context.Users
                .Include(u => u.ManagedEnterprises)
                .Include(u => u.UserEnterprises)
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return Enumerable.Empty<Enterprise>();

            if (userType != UserType.Admin && currentUserId != userId && user.ManagerId != currentUserId)
                return Enumerable.Empty<Enterprise>();

            return user.ManagedEnterprises.Concat(user.UserEnterprises).GroupBy(e => e.Id).Select(g => g.First()).ToList();
        }

        public async Task<bool> AddUserToEnterpriseAsync(int userId, int enterpriseId, string token)
        {
            var currentUserId = GetUserIdFromToken(token);
            var userType = GetUserTypeFromToken(token);
            if (currentUserId == null) return false;

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return false;

            var enterprise = await _context.Enterprises.Include(e => e.Users).FirstOrDefaultAsync(e => e.Id == enterpriseId);
            if (enterprise == null) return false;

            if (userType != UserType.Admin && (user.ManagerId != currentUserId || enterprise.ManagerId != currentUserId))
                return false;

            if (enterprise.Users.Any(u => u.Id == userId)) return false;
            if (enterprise.MaxUsers.HasValue && enterprise.Users.Count >= enterprise.MaxUsers.Value)
                throw new InvalidOperationException("A empresa atingiu o número máximo de usuários permitido (MaxUsers).");

            enterprise.Users.Add(user);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RemoveUserFromEnterpriseAsync(int userId, int enterpriseId, string token)
        {
            var currentUserId = GetUserIdFromToken(token);
            var userType = GetUserTypeFromToken(token);
            if (currentUserId == null) return false;

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return false;

            var enterprise = await _context.Enterprises.Include(e => e.Users).FirstOrDefaultAsync(e => e.Id == enterpriseId);
            if (enterprise == null) return false;

            if (userType != UserType.Admin && (user.ManagerId != currentUserId || enterprise.ManagerId != currentUserId))
                return false;

            var member = enterprise.Users.FirstOrDefault(u => u.Id == userId);
            if (member == null) return false;

            enterprise.Users.Remove(member);
            await _context.SaveChangesAsync();
            return true;
        }

        private int? GetUserIdFromToken(string token)
        {
            try
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);
                var claim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
                return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
            }
            catch { return null; }
        }

        private UserType GetUserTypeFromToken(string token)
        {
            try
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);
                var claim = jwt.Claims.FirstOrDefault(c => c.Type == "UserType");
                return claim != null && Enum.TryParse<UserType>(claim.Value, out var t) ? t : UserType.User;
            }
            catch { return UserType.User; }
        }
    }
}
