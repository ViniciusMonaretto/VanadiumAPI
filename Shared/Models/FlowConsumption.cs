namespace Shared.Models
{
    public class FlowConsumption
    {
        public int PanelId { get; set; }
        public DateTime LastUpdated { get; set; }
        public double DayConsumption { get; set; }
        public double WeekConsumption { get; set; }
        public double MonthConsumption { get; set; }
        public double LastMonthConsumption { get; set; }
        /// <summary>Readings from the last hour for this panel.</summary>
        public List<PanelReading> ReadingsLastHour { get; set; } = new();
    }
}