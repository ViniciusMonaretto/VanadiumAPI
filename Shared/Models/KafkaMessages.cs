namespace Shared.Models
{
    public enum PanelChangeAction
    {
        Create,
        Update,
        Delete
    }
    public class PanelChangeMessage
    {
        public PanelChangeAction Action { get; set; }
        public Panel Panel { get; set; }
    }
}