namespace VanadiumAPI.DTOs
{
    public class CreateUserDto
    {
        public required string Name { get; set; }
        public required string Username { get; set; }
        public required string Email { get; set; }
        public string? Company { get; set; }
        public required string Password { get; set; }
        public Shared.Models.UserType UserType { get; set; }
        public int? ManagerId { get; set; }
        public int? MaxGraphs { get; set; }
        public int? MaxPanels { get; set; }
    }
}
