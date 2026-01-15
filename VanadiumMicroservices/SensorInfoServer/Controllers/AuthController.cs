using Microsoft.AspNetCore.Mvc;
using SensorInfoServer.DTOs;
using SensorInfoServer.Services;
using Data.Sqlite;
using Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace SensorInfoServer.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly SqliteDataContext _context;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, SqliteDataContext context, ILogger<AuthController> logger)
        {
            _authService = authService;
            _context = context;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto loginDto)
        {
            try
            {
                if (loginDto == null || string.IsNullOrEmpty(loginDto.Email) || string.IsNullOrEmpty(loginDto.Password))
                {
                    return BadRequest(new { message = "Email and password are required" });
                }

                var result = await _authService.LoginAsync(loginDto);

                if (result == null)
                {
                    return Unauthorized(new { message = "Invalid email or password" });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return StatusCode(500, new { message = "Error during login", error = ex.Message });
            }
        }

        [HttpPost("validate")]
        public ActionResult ValidateToken([FromBody] ValidateTokenDto? validateTokenDto)
        {
            try
            {
                // Try to get token from body first, then from Authorization header
                string? token = validateTokenDto?.Token;

                if (string.IsNullOrWhiteSpace(token))
                {
                    // Try to get from Authorization header
                    var authHeader = Request.Headers["Authorization"].FirstOrDefault();
                    if (authHeader != null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        token = authHeader.Substring("Bearer ".Length).Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    return BadRequest(new { message = "Token is required", valid = false });
                }

                var isValid = _authService.ValidateToken(token);

                if (isValid)
                {
                    return Ok(new { message = "Token is valid", valid = true });
                }
                else
                {
                    return Unauthorized(new { message = "Token is invalid or expired", valid = false });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token validation");
                return StatusCode(500, new { message = "Error during token validation", error = ex.Message, valid = false });
            }
        }
    }
}

