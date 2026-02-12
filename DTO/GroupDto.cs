using Shared.Models;

namespace VanadiumAPI.DTO
{
    public class GroupDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<PanelDto> Panels { get; set; } = new List<PanelDto>();

        public GroupDto(Group group)
        {
            Id = group.Id;
            Name = group.Name;
            Panels = group.Panels?.Select(p => new PanelDto(p)).ToList() ?? new List<PanelDto>();
        }
    }
}
