using System.Security.Claims;
using Data.Sqlite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using VanadiumAPI.DTOs;
using VanadiumAPI.Services;

namespace VanadiumAPI.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly SqliteDataContext _context;
        private readonly IAuthService _authService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(SqliteDataContext context, IAuthService authService, ILogger<UsersController> logger)
        {
            _context = context;
            _authService = authService;
            _logger = logger;
        }

        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<UserInfo>> CreateUser([FromBody] CreateUserDto createUserDto)
        {
            if (createUserDto == null) return BadRequest(new { message = "User data is required" });
            var existingUser = await _authService.GetUserByEmailAsync(createUserDto.Email);
            if (existingUser != null) return Conflict(new { message = "Email already exists" });
            if (createUserDto.ManagerId.HasValue)
            {
                var manager = await _context.Users.FindAsync(createUserDto.ManagerId.Value);
                if (manager == null) return BadRequest(new { message = "Invalid manager ID" });
                if (manager.UserType != UserType.Admin && manager.UserType != UserType.Manager)
                    return BadRequest(new { message = "Manager must be an Admin or Manager" });
            }
            var newUser = new UserInfo
            {
                Name = createUserDto.Name,
                Email = createUserDto.Email,
                Company = createUserDto.Company,
                PasswordHash = AuthService.HashPassword(createUserDto.Password),
                UserType = createUserDto.UserType,
                ManagerId = createUserDto.ManagerId
            };
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetUserById), new { id = newUser.Id }, newUser);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserInfo>>> GetAllUsers()
        {
            var users = await _context.Users.Include(u => u.ManagedUsers).ToListAsync();
            foreach (var user in users) user.PasswordHash = string.Empty;
            return Ok(users);
        }

        [HttpGet("managed")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<UserInfo>>> GetManagedUsers()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (claim == null || !int.TryParse(claim, out var managerId))
                return Unauthorized(new { message = "Invalid user token" });
            var users = await _context.Users.Where(u => u.ManagerId == managerId).ToListAsync();
            foreach (var user in users) user.PasswordHash = string.Empty;
            return Ok(users);
        }

        [HttpPost("managed")]
        [Authorize(Policy = "ManagerOrAdmin")]
        public async Task<ActionResult<UserInfo>> CreateManagedUser([FromBody] CreateUserDto createUserDto)
        {
            if (createUserDto == null) return BadRequest(new { message = "User data is required" });
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (claim == null || !int.TryParse(claim, out var managerId))
                return Unauthorized(new { message = "Invalid user token" });
            var existingUser = await _authService.GetUserByEmailAsync(createUserDto.Email);
            if (existingUser != null) return Conflict(new { message = "Email already exists" });
            var newUser = new UserInfo
            {
                Name = createUserDto.Name,
                UserName = createUserDto.Username,
                Email = createUserDto.Email,
                Company = createUserDto.Company,
                PasswordHash = AuthService.HashPassword(createUserDto.Password),
                UserType = createUserDto.UserType,
                ManagerId = managerId
            };
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();
            newUser.PasswordHash = string.Empty;
            return CreatedAtAction(nameof(GetUserById), new { id = newUser.Id }, newUser);
        }

        [HttpDelete("managed/{id}")]
        [Authorize]
        public async Task<ActionResult> DeleteManagedUser(int id)
        {
            var currentClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userTypeClaim = User.FindFirst("UserType")?.Value;
            if (currentClaim == null || !int.TryParse(currentClaim, out var currentUserId))
                return Unauthorized(new { message = "Invalid user token" });
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "User not found" });
            bool isAdmin = userTypeClaim == UserType.Admin.ToString();
            bool isManagerOfUser = user.ManagerId == currentUserId;
            if (!isAdmin && !isManagerOfUser) return Forbid();
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<UserInfo>> GetUserById(int id)
        {
            var user = await _context.Users.Include(u => u.ManagedUsers).FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound(new { message = "User not found" });
            user.PasswordHash = string.Empty;
            return Ok(user);
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "User not found" });
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{id}/change-password")]
        [Authorize]
        public async Task<ActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto changePasswordDto)
        {
            if (changePasswordDto == null) return BadRequest(new { message = "Password data is required" });
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "User not found" });
            var currentClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            bool isAdmin = User.FindFirst("UserType")?.Value == UserType.Admin.ToString();
            if (currentClaim == null || !int.TryParse(currentClaim, out var currentUserId))
                return Unauthorized(new { message = "Invalid user token" });
            bool isChangingOwnPassword = currentUserId == id;
            if (!isAdmin && !isChangingOwnPassword) return Forbid();
            if (isChangingOwnPassword && !AuthService.VerifyPassword(changePasswordDto.CurrentPassword, user.PasswordHash))
                return Unauthorized(new { message = "Current password is incorrect" });
            user.PasswordHash = AuthService.HashPassword(changePasswordDto.NewPassword);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Password changed successfully" });
        }

        [HttpGet("{userId}/enterprises")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<Enterprise>>> GetUserEnterprises(int userId)
        {
            var currentClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userTypeClaim = User.FindFirst("UserType")?.Value;
            if (currentClaim == null || !int.TryParse(currentClaim, out var currentUserId))
                return Unauthorized(new { message = "Invalid user token" });
            var user = await _context.Users.Include(u => u.ManagedEnterprises).Include(u => u.UserEnterprises)
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return NotFound(new { message = "User not found" });
            bool isAdmin = userTypeClaim == UserType.Admin.ToString();
            bool isSelf = currentUserId == userId;
            bool isManagerOfUser = user.ManagerId == currentUserId;
            if (!isAdmin && !isSelf && !isManagerOfUser) return Forbid();
            var enterprises = user.ManagedEnterprises.Concat(user.UserEnterprises).GroupBy(e => e.Id).Select(g => g.First()).ToList();
            return Ok(enterprises);
        }

        [HttpPost("{userId}/enterprises/{enterpriseId}")]
        [Authorize]
        public async Task<ActionResult> AddUserToEnterprise(int userId, int enterpriseId)
        {
            var currentClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userTypeClaim = User.FindFirst("UserType")?.Value;
            if (currentClaim == null || !int.TryParse(currentClaim, out var currentUserId))
                return Unauthorized(new { message = "Invalid user token" });
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound(new { message = "User not found" });
            var enterprise = await _context.Enterprises.Include(e => e.Users).FirstOrDefaultAsync(e => e.Id == enterpriseId);
            if (enterprise == null) return NotFound(new { message = "Enterprise not found" });
            bool isAdmin = userTypeClaim == UserType.Admin.ToString();
            bool isManagerOfUser = user.ManagerId == currentUserId;
            bool isManagerOfEnterprise = enterprise.ManagerId == currentUserId;
            if (!isAdmin && !(isManagerOfUser && isManagerOfEnterprise)) return Forbid();
            if (enterprise.Users.Any(u => u.Id == userId)) return Conflict(new { message = "User already in this enterprise" });
            if (enterprise.MaxUsers.HasValue && enterprise.Users.Count >= enterprise.MaxUsers.Value)
                return BadRequest(new { message = "Enterprise has reached its maximum number of users (MaxUsers)" });
            enterprise.Users.Add(user);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{userId}/enterprises/{enterpriseId}")]
        [Authorize]
        public async Task<ActionResult> RemoveUserFromEnterprise(int userId, int enterpriseId)
        {
            var currentClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userTypeClaim = User.FindFirst("UserType")?.Value;
            if (currentClaim == null || !int.TryParse(currentClaim, out var currentUserId))
                return Unauthorized(new { message = "Invalid user token" });
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound(new { message = "User not found" });
            var enterprise = await _context.Enterprises.Include(e => e.Users).FirstOrDefaultAsync(e => e.Id == enterpriseId);
            if (enterprise == null) return NotFound(new { message = "Enterprise not found" });
            bool isAdmin = userTypeClaim == UserType.Admin.ToString();
            bool isManagerOfUser = user.ManagerId == currentUserId;
            bool isManagerOfEnterprise = enterprise.ManagerId == currentUserId;
            if (!isAdmin && !(isManagerOfUser && isManagerOfEnterprise)) return Forbid();
            var member = enterprise.Users.FirstOrDefault(u => u.Id == userId);
            if (member == null) return NotFound(new { message = "User is not in this enterprise" });
            enterprise.Users.Remove(member);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
