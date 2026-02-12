using Shared.Models;

namespace VanadiumAPI.DTO
{
    public class UserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public UserType UserType { get; set; }
        public int? ManagerId { get; set; }
        public List<EnterpriseDto> Enterprises { get; set; } = new List<EnterpriseDto>();
        public List<UserDto> ManagedUsers { get; set; } = new List<UserDto>();

        public UserDto(UserInfo user)
        {
            Id = user.Id;
            Name = user.Name;
            Email = user.Email;
            UserType = user.UserType;
            ManagerId = user.ManagerId;
            Enterprises = user.UserEnterprises?.Select(e => new EnterpriseDto(e)).ToList() ?? new List<EnterpriseDto>();
            ManagedUsers = user.ManagedUsers?.Select(u => new UserDto(u)).ToList() ?? new List<UserDto>();
        }

        public UserDto() { }
    }
}
