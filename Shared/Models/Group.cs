namespace Shared.Models
{
    public class Group
    {
        public int Id {get; set;}
        public string Name {get; set;}
        public ICollection<Panel> Panels {get; set;}
    }
}