using Microsoft.AspNetCore.Mvc;
using Data.Mongo;
using Shared.Models;

namespace API.Controllers
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

        /// <summary>
        /// Get panel readings by panel ID with optional date range filtering
        /// </summary>
        [HttpGet("{panelId}")]
        public async Task<ActionResult<IEnumerable<PanelReading>>> GetPanelReadings(
            int panelId,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            var readings = await _repository.GetPanelReadingsByPanelId(panelId, startDate, endDate);

            if (readings == null || !readings.Any())
                return NotFound($"No readings found for panel {panelId}");

            return Ok(readings);
        }

        [HttpPost("multiple")]
        public async Task<ActionResult<Dictionary<int, List<PanelReading>>>> GetMultiplePanelReadings(
            [FromBody] MultiplePanelReadingsRequest request)
        {
            if (request == null || request.PanelIds == null || !request.PanelIds.Any())
                return BadRequest("Panel IDs cannot be null or empty");

            var readings = await _repository.GetPanelReadingsByPanelIds(
                request.PanelIds,
                request.StartDate,
                request.EndDate);

            if (readings == null || !readings.Any())
                return NotFound("No readings found for the specified panels");

            return Ok(readings);
        }
    }
    
    public class MultiplePanelReadingsRequest
    {
        public IEnumerable<int> PanelIds { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}