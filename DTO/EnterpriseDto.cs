using Shared.Models;

namespace VanadiumAPI.DTO
{
    public class EnterpriseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public EnterpriseDto(Enterprise enterprise)
        {
            Id = enterprise.Id;
            Name = enterprise.Name;
        }
    }
}
