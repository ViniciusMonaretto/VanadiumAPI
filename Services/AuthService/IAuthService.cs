using VanadiumAPI.DTOs;
using Shared.Models;

namespace VanadiumAPI.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto?> LoginAsync(LoginDto loginDto);
        Task<bool> ValidateTokenAsync(string token);
        string GenerateJwtToken(UserInfo user);
        Task<UserInfo?> GetUserByEmailAsync(string email);
        Task<UserInfo?> GetUserByUsernameAsync(string username);
        Task<UserInfo?> GetUserByIdAsync(int id);
        bool ValidateToken(string token);
    }
}
