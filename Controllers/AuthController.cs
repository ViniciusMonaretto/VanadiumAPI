using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VanadiumAPI.DTOs;
using VanadiumAPI.Services;

namespace VanadiumAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [AllowAnonymous]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto loginDto)
        {
            if (loginDto == null || string.IsNullOrEmpty(loginDto.Username) || string.IsNullOrEmpty(loginDto.Password))
                return BadRequest(new { message = "Username and password are required" });
            var result = await _authService.LoginAsync(loginDto);
            if (result == null)
                return Unauthorized(new { message = "Invalid email or password" });
            return Ok(result);
        }

        [HttpPost("validate")]
        public ActionResult ValidateToken([FromBody] ValidateTokenDto validateTokenDto)
        {
            string token = validateTokenDto?.Token ?? "";
            if (string.IsNullOrWhiteSpace(token))
            {
                var authHeader = Request.Headers.Authorization.FirstOrDefault();
                if (authHeader != null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = authHeader.Substring(7).Trim();
            }
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest(new { message = "Token is required", valid = false });
            bool isValid = _authService.ValidateToken(token);
            if (isValid)
                return Ok(new { message = "Token is valid", valid = true });
            return Unauthorized(new { message = "Token is invalid or expired", valid = false });
        }
    }
}
