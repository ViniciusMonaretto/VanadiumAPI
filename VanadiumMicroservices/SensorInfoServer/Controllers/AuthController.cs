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
    }
}

