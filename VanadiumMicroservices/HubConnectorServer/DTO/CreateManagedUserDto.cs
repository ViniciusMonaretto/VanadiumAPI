using Shared.Models;

namespace HubConnectorServer.DTO
{
    public class CreateManagedUserDto
    {
        public required string Name { get; set; }
        public required string Username { get; set; }
        public required string Email { get; set; }
        public string? Company { get; set; }
        public required string Password { get; set; }
        public UserType UserType { get; set; } = UserType.User;
    }
}
