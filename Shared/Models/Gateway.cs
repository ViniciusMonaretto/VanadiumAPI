namespace Shared.Models
{
    public class Gateway
    {
        public int Id { get; set; }
        public string GatewayId { get; set; }
        public int EnterpriseId { get; set; }
        public Enterprise Enterprise { get; set; }
        public IEnumerable<Panel> Panels { get; set; }

        // Set once the device has responded to GET_SENSORS and its available sensors were persisted below.
        public bool IsSetup { get; set; }
        public List<PanelType> AvailableSensors { get; set; } = new();
    }
}