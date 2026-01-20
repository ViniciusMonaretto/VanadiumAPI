using SensorInfoServer.DTOs;
using Shared.Models;

namespace SensorInfoServer.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto?> LoginAsync(LoginDto loginDto);
        string GenerateJwtToken(UserInfo user);
        Task<UserInfo?> GetUserByEmailAsync(string email);
        Task<UserInfo?> GetUserByUsernameAsync(string username);
        Task<UserInfo?> GetUserByIdAsync(int id);
        bool ValidateToken(string token);
    }
}

