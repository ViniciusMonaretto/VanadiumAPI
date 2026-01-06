namespace Shared.Models
{
    public class UserGroup
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Group> Groups { get; set; } = new List<Group>();
        public List<UserInfo> Users { get; set; } = new List<UserInfo>();
    }
}