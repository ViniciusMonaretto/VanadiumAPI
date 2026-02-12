using Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace VanadiumAPI.Controllers
{
    [ApiController]
    [Route("api/alarms")]
    public class AlarmsController : ControllerBase
    {
        private readonly IPanelInfoRepository _repository;

        public AlarmsController(IPanelInfoRepository repository) => _repository = repository;

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Alarm>>> GetAllAlarms()
        {
            try { return Ok(await _repository.GetAllAlarms()); }
            catch (Exception ex) { return StatusCode(500, new { message = "Error retrieving alarms", error = ex.Message }); }
        }

        [HttpGet("events")]
        public async Task<ActionResult<IEnumerable<AlarmEvent>>> GetAllAlarmEvents()
        {
            try { return Ok(await _repository.GetAllAlarmEvents()); }
            catch (Exception ex) { return StatusCode(500, new { message = "Error retrieving alarm events", error = ex.Message }); }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Alarm>> GetAlarmById(int id)
        {
            var alarm = await _repository.GetAlarmById(id);
            if (alarm == null) return NotFound(new { message = "Alarm not found" });
            return Ok(alarm);
        }

        [HttpPost]
        public async Task<ActionResult<Alarm>> CreateAlarm([FromBody] Alarm alarm)
        {
            if (alarm == null) return BadRequest(new { message = "Alarm data is required" });
            _repository.Add(alarm);
            if (await _repository.SaveAll()) return CreatedAtAction(nameof(GetAlarmById), new { id = alarm.Id }, alarm);
            return BadRequest(new { message = "Failed to create alarm" });
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteAlarm(int id)
        {
            var alarm = await _repository.GetAlarmById(id);
            if (alarm == null) return NotFound(new { message = "Alarm not found" });
            _repository.Delete(alarm);
            if (await _repository.SaveAll()) return NoContent();
            return BadRequest(new { message = "Failed to delete alarm" });
        }
    }
}
