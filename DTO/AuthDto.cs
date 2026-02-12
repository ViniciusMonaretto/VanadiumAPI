using Shared.Models;
using VanadiumAPI.DTOs;

namespace VanadiumAPI.DTO
{
    public class AuthDto
    {
        public string Token { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public UserType UserType { get; set; }
        public int? ManagerId { get; set; }
        public List<EnterpriseDto> Enterprises { get; set; } = new List<EnterpriseDto>();

        public AuthDto(AuthResponseDto authResponse)
        {
            Token = authResponse.Token;
            UserId = authResponse.UserId;
            Email = authResponse.Email;
            Name = authResponse.Name;
            UserType = authResponse.UserType;
            ManagerId = authResponse.ManagerId;
            Enterprises = authResponse.Enterprises.Select(e => new EnterpriseDto(e)).ToList();
        }

        public AuthDto() { }
    }
}
