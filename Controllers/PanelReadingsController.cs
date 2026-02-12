using Data.Mongo;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace VanadiumAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PanelReadingsController : ControllerBase
    {
        private readonly IPanelReadingRepository _repository;

        public PanelReadingsController(IPanelReadingRepository repository)
        {
            _repository = repository;
        }

        [HttpGet("{panelId}")]
        public async Task<ActionResult<IEnumerable<PanelReading>>> GetPanelReadings(
            int panelId,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            var readings = await _repository.GetPanelReadingsByPanelId(panelId, startDate, endDate);
            return Ok(readings ?? Enumerable.Empty<PanelReading>());
        }

        [HttpPost("multiple")]
        public async Task<ActionResult<Dictionary<int, List<PanelReading>>>> GetMultiplePanelReadings([FromBody] MultiplePanelReadingsRequest? request)
        {
            if (request == null || request.PanelIds == null || !request.PanelIds.Any())
                return BadRequest("Panel IDs cannot be null or empty");

            var readings = await _repository.GetPanelReadingsByPanelIds(
                request.PanelIds,
                request.StartDate,
                request.EndDate);
            return Ok(readings ?? new Dictionary<int, List<PanelReading>>());
        }
    }

    public class MultiplePanelReadingsRequest
    {
        public IEnumerable<int> PanelIds { get; set; } = Enumerable.Empty<int>();
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
