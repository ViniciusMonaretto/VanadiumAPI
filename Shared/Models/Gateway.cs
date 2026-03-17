namespace Shared.Models
{
    public class Gateway
    {
        public int Id { get; set; }
        public string GatewayId { get; set; }
        public int EnterpriseId { get; set; }
        public Enterprise Enterprise { get; set; }
        public IEnumerable<Panel> Panels { get; set; }
    }
}