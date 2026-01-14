using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data.Sqlite;
using SensorInfoServer.DTOs;
using SensorInfoServer.Services;
using Shared.Models;
using System.Security.Claims;

namespace SensorInfoServer.Controllers
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
            try
            {
                if (createUserDto == null)
                {
                    return BadRequest(new { message = "User data is required" });
                }

                // Check if email already exists
                var existingUser = await _authService.GetUserByEmailAsync(createUserDto.Email);
                if (existingUser != null)
                {
                    return Conflict(new { message = "Email already exists" });
                }

                // Validate manager if provided
                if (createUserDto.ManagerId.HasValue)
                {
                    var manager = await _context.Users.FindAsync(createUserDto.ManagerId.Value);
                    if (manager == null)
                    {
                        return BadRequest(new { message = "Invalid manager ID" });
                    }
                    if (manager.UserType != UserType.Admin && manager.UserType != UserType.Manager)
                    {
                        return BadRequest(new { message = "Manager must be an Admin or Manager" });
                    }
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                return StatusCode(500, new { message = "Error creating user", error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserInfo>>> GetAllUsers()
        {
            try
            {
                var users = await _context.Users
                    .Include(u => u.ManagedUsers)
                    .ToListAsync();

                // Remove password hashes from response
                foreach (var user in users)
                {
                    user.PasswordHash = string.Empty;
                }

                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users");
                return StatusCode(500, new { message = "Error retrieving users", error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<UserInfo>> GetUserById(int id)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.ManagedUsers)
                    .FirstOrDefaultAsync(u => u.Id == id);

                if (user == null)
                {
                    return NotFound(new { message = $"User with id {id} not found" });
                }

                // Remove password hash from response
                user.PasswordHash = string.Empty;

                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user");
                return StatusCode(500, new { message = "Error retrieving user", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult> DeleteUser(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);

                if (user == null)
                {
                    return NotFound(new { message = $"User with id {id} not found" });
                }

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user");
                return StatusCode(500, new { message = "Error deleting user", error = ex.Message });
            }
        }

        [HttpPost("{id}/change-password")]
        [Authorize]
        public async Task<ActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto changePasswordDto)
        {
            try
            {
                if (changePasswordDto == null)
                {
                    return BadRequest(new { message = "Password data is required" });
                }

                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound(new { message = $"User with id {id} not found" });
                }

                // Security check: Users can only change their own password, unless they're Admin
                var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var isAdmin = User.FindFirst("UserType")?.Value == UserType.Admin.ToString();
                
                if (currentUserIdClaim == null || !int.TryParse(currentUserIdClaim, out var currentUserId))
                {
                    return Unauthorized(new { message = "Invalid user token" });
                }

                var isChangingOwnPassword = currentUserId == id;

                // Non-admin users can only change their own password
                if (!isAdmin && !isChangingOwnPassword)
                {
                    return Forbid("You can only change your own password");
                }

                // Verify current password (required when changing own password, or when non-admin)
                if (isChangingOwnPassword && !AuthService.VerifyPassword(changePasswordDto.CurrentPassword, user.PasswordHash))
                {
                    return Unauthorized(new { message = "Current password is incorrect" });
                }

                // Update password
                user.PasswordHash = AuthService.HashPassword(changePasswordDto.NewPassword);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Password changed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password");
                return StatusCode(500, new { message = "Error changing password", error = ex.Message });
            }
        }
    }
}

