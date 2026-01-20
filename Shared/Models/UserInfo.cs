namespace Shared.Models
{
    public enum UserType
    {
        Admin = 0,
        Manager = 1,
        User = 2
    }

    public class UserInfo
    {
        public int Id { get; set; }
        public string? UserName { get; set; }
        public required string Name { get; set; }
        public required string Email { get; set; }
        public string? Company { get; set; }
        public UserType UserType { get; set; }
        public required string PasswordHash { get; set; }

        // Manager relationship (self-referencing)
        public int? ManagerId { get; set; }
        public ICollection<UserInfo> ManagedUsers { get; set; } = new List<UserInfo>();
        
        // Enterprises where this user is the manager (one-to-many)
        public List<Enterprise> ManagedEnterprises { get; set; } = new List<Enterprise>();
        
        // Enterprises where this user is a member (many-to-many)
        public List<Enterprise> UserEnterprises { get; set; } = new List<Enterprise>();
    }
}

