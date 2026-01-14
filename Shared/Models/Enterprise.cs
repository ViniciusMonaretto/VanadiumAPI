namespace Shared.Models
{
    public class Enterprise
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Group> Groups { get; set; } = new List<Group>();
        public int ManagerId { get; set; }
        public UserInfo Manager { get; set; } = new UserInfo { Name = string.Empty, Email = string.Empty, PasswordHash = string.Empty };
        public List<UserInfo> Users { get; set; } = new List<UserInfo>();

        // User limits (null means unlimited, only for Admin)
        public int? MaxUsers { get; set; }
        public int? MaxPanels { get; set; }
    }
}