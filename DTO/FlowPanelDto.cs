using Shared.Models;

namespace VanadiumAPI.DTO
{
    public class FlowConsumptionDto
    {
        public int PanelId { get; set; }
        public DateTime LastUpdated { get; set; }
        public double DayConsumption { get; set; }
        public double WeekConsumption { get; set; }
        public double MonthConsumption { get; set; }
        public double LastMonthConsumption { get; set; }

        public static FlowConsumptionDto FromModel(FlowConsumption model) => new()
        {
            PanelId = model.PanelId,
            LastUpdated = model.LastUpdated,
            DayConsumption = model.DayConsumption,
            WeekConsumption = model.WeekConsumption,
            MonthConsumption = model.MonthConsumption,
            LastMonthConsumption = model.LastMonthConsumption,
        };
    }
}
