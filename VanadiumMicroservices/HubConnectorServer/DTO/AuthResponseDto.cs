using Shared.Models;

namespace HubConnectorServer.DTO
{
    public class AuthResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public UserType UserType { get; set; }
        public int? ManagerId { get; set; }
    }
}

