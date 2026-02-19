using Shared.Models;

namespace Data.Mongo
{
    public interface IPanelReadingRepository
    {
        Task AddAsync(PanelReading panelReading);
        Task AddAsync(IEnumerable<PanelReading> panelReadings);
        Task<IEnumerable<PanelReading>> GetPanelReadingsByPanelId(int panelId,
                                                                  DateTime? startDate,
                                                                  DateTime? endDate);
        Task<Dictionary<int, List<PanelReading>>> GetPanelReadingsByPanelIds(IEnumerable<int> panelIds,
                                                                       DateTime? startDate,
                                                                       DateTime? endDate);

        Task<Dictionary<int, List<FlowConsumption>>> GetFlowConsumptionsOfPanels(IEnumerable<int> panelIds);
    }
}
