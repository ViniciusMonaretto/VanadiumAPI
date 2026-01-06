namespace HubConnectorServer.DTO
{
    public class AuthDto
    {
        public string Token { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Shared.Models.UserType UserType { get; set; }
        public int? ManagerId { get; set; }

        public AuthDto(AuthResponseDto authResponse)
        {
            Token = authResponse.Token;
            UserId = authResponse.UserId;
            Email = authResponse.Email;
            Name = authResponse.Name;
            UserType = authResponse.UserType;
            ManagerId = authResponse.ManagerId;
        }

        public AuthDto()
        {
        }
    }
}

