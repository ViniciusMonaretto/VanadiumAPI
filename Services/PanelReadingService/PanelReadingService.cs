using Data.Mongo;
using Shared.Models;

namespace VanadiumAPI.Services
{
    public class PanelReadingService : IPanelReadingService
    {
        private readonly IPanelReadingRepository _repository;
        private readonly ILogger<PanelReadingService> _logger;

        public PanelReadingService(IPanelReadingRepository repository, ILogger<PanelReadingService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<IEnumerable<PanelReading>> GetPanelReadingsAsync(int panelId, DateTime? startDate = null, DateTime? endDate = null)
        {
            return await _repository.GetPanelReadingsByPanelId(panelId, startDate, endDate);
        }

        public async Task<Dictionary<int, List<PanelReading>>> GetMultiplePanelReadingsAsync(IEnumerable<int> panelIds, DateTime? startDate = null, DateTime? endDate = null)
        {
            return await _repository.GetPanelReadingsByPanelIds(panelIds, startDate, endDate);
        }

        public async Task<Dictionary<int, List<FlowConsumption>>> GetFlowConsumptionsOfPanelsAsync(IEnumerable<int> panelIds)
        {
            return await _repository.GetFlowConsumptionsOfPanels(panelIds);
        }
    }
}
