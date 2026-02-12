using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VanadiumAPI.DTOs;
using Shared.Models;

namespace VanadiumAPI.Services
{
    public class AuthService : IAuthService
    {
        private readonly SqliteDataContext _context;
        private readonly JwtSettings _jwtSettings;
        private readonly ILogger<AuthService> _logger;

        public AuthService(SqliteDataContext context, IOptions<JwtSettings> jwtSettings, ILogger<AuthService> logger)
        {
            _context = context;
            _jwtSettings = jwtSettings.Value;
            _logger = logger;
        }

        public async Task<AuthResponseDto?> LoginAsync(LoginDto loginDto)
        {
            var user = await GetUserByEmailAsync(loginDto.Username);
            if (user == null)
            {
                user = await GetUserByUsernameAsync(loginDto.Username);
                if (user == null)
                {
                    _logger.LogWarning("Login attempt with non-existent username: {Username}", loginDto.Username);
                    return null;
                }
            }

            if (!VerifyPassword(loginDto.Password, user.PasswordHash))
            {
                _logger.LogWarning("Invalid password attempt for user: {Email}", loginDto.Username);
                return null;
            }

            var token = GenerateJwtToken(user);
            List<Enterprise> enterprises;
            if (user.UserType == UserType.Admin)
                enterprises = await _context.Enterprises.ToListAsync();
            else
                enterprises = user.ManagedEnterprises.Concat(user.UserEnterprises).GroupBy(e => e.Id).Select(g => g.First()).ToList();

            return new AuthResponseDto
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                Name = user.Name,
                UserType = user.UserType,
                ManagerId = user.ManagerId,
                Enterprises = enterprises
            };
        }

        public Task<bool> ValidateTokenAsync(string token)
        {
            return Task.FromResult(ValidateToken(token));
        }

        public string GenerateJwtToken(UserInfo user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim("UserType", user.UserType.ToString()),
                new Claim("ManagerId", user.ManagerId?.ToString() ?? "")
            };
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
                signingCredentials: creds
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task<UserInfo?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .Include(u => u.ManagedEnterprises)
                .Include(u => u.UserEnterprises)
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<UserInfo?> GetUserByUsernameAsync(string username)
        {
            return await _context.Users
                .Include(u => u.ManagedEnterprises)
                .Include(u => u.UserEnterprises)
                .FirstOrDefaultAsync(u => u.UserName == username);
        }

        public async Task<UserInfo?> GetUserByIdAsync(int id)
        {
            return await _context.Users
                .Include(u => u.ManagedUsers)
                .Include(u => u.ManagedEnterprises)
                .Include(u => u.UserEnterprises)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public static string HashPassword(string password)
        {
            byte[] salt = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            byte[] hash = pbkdf2.GetBytes(32);
            byte[] hashBytes = new byte[64];
            Array.Copy(salt, 0, hashBytes, 0, 32);
            Array.Copy(hash, 0, hashBytes, 32, 32);
            return Convert.ToBase64String(hashBytes);
        }

        public static bool VerifyPassword(string password, string storedHash)
        {
            try
            {
                byte[] hashBytes = Convert.FromBase64String(storedHash);
                byte[] salt = new byte[32];
                Array.Copy(hashBytes, 0, salt, 0, 32);
                using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
                byte[] hash = pbkdf2.GetBytes(32);
                for (int i = 0; i < 32; i++)
                    if (hashBytes[i + 32] != hash[i]) return false;
                return true;
            }
            catch { return false; }
        }

        public bool ValidateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_jwtSettings.SecretKey);
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _jwtSettings.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
                tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token validation failed");
                return false;
            }
        }
    }
}
