using Shared.Models;

namespace VanadiumAPI.Services
{
    public interface IPanelReadingService
    {
        Task<IEnumerable<PanelReading>> GetPanelReadingsAsync(int panelId, DateTime? startDate = null, DateTime? endDate = null);
        Task<Dictionary<int, List<PanelReading>>> GetMultiplePanelReadingsAsync(IEnumerable<int> panelIds, DateTime? startDate = null, DateTime? endDate = null);
    }
}
