using Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;
using VanadiumAPI.Services;

namespace VanadiumAPI.Controllers
{
    [ApiController]
    [Route("api/sensorInfo")]
    public class PanelInfoController : ControllerBase
    {
        private readonly IPanelInfoRepository _repository;
        private readonly IPanelBroadcastService _broadcastService;
        private readonly ILogger<PanelInfoController> _logger;

        public PanelInfoController(
            IPanelInfoRepository repository,
            IPanelBroadcastService broadcastService,
            ILogger<PanelInfoController> logger)
        {
            _repository = repository;
            _broadcastService = broadcastService;
            _logger = logger;
        }

        private async Task SendPanelChangeMessage(PanelChangeAction action, Panel panel)
        {
            try
            {
                await _broadcastService.BroadcastPanelChange(new PanelChangeMessage { Action = action, Panel = panel });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting panel change");
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Panel>>> GetAllPanels()
        {
            try
            {
                var panels = await _repository.GetAllPanels();
                return Ok(panels);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving panels", error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Panel>> GetPanelById(int id)
        {
            try
            {
                var panel = await _repository.GetPanelById(id);
                if (panel == null)
                    return NotFound(new { message = $"Panel with id {id} not found" });
                return Ok(panel);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving panel", error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<Panel>> CreatePanel([FromBody] Panel? panel)
        {
            try
            {
                if (panel == null)
                    return BadRequest(new { message = "Panel data is required" });
                _repository.Add(panel);
                if (await _repository.SaveAll())
                {
                    await SendPanelChangeMessage(PanelChangeAction.Create, panel);
                    return CreatedAtAction(nameof(GetPanelById), new { id = panel.Id }, panel);
                }
                return BadRequest(new { message = "Failed to create panel" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error creating panel", error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult> UpdatePanel(int id, [FromBody] Panel? panel)
        {
            try
            {
                if (panel == null)
                    return BadRequest(new { message = "Panel data is required" });
                var existing = await _repository.GetPanelById(id);
                if (existing == null)
                    return NotFound(new { message = $"Panel with id {id} not found" });
                if (await _repository.SaveAll())
                {
                    await SendPanelChangeMessage(PanelChangeAction.Update, panel);
                    return NoContent();
                }
                return BadRequest(new { message = "Failed to update panel" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error updating panel", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeletePanel(int id)
        {
            try
            {
                var panel = await _repository.GetPanelById(id);
                if (panel == null)
                    return NotFound(new { message = $"Panel with id {id} not found" });
                _repository.Delete(panel);
                if (await _repository.SaveAll())
                {
                    await SendPanelChangeMessage(PanelChangeAction.Delete, panel);
                    return NoContent();
                }
                return BadRequest(new { message = "Failed to delete panel" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error deleting panel", error = ex.Message });
            }
        }
    }
}
