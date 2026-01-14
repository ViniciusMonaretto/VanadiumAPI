using Shared.Models;

namespace HubConnectorServer.DTO
{
    public class GroupDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<PanelDto> Panels { get; set; }

        public GroupDto(Group group)
        {
            Id = group.Id;
            Name = group.Name;
            Panels = group.Panels.Select(p => new PanelDto(p)).ToList();
        }
    }
}