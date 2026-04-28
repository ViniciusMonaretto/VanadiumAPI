using Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;
using VanadiumAPI.Services.AlarmRegistry;

namespace VanadiumAPI.Controllers
{
    [ApiController]
    [Route("api/alarms")]
    public class AlarmsController : ControllerBase
    {
        private readonly IPanelInfoRepository _repository;
        private readonly IAlarmRegistryService _alarmRegistry;

        public AlarmsController(IPanelInfoRepository repository, IAlarmRegistryService alarmRegistry)
        {
            _repository = repository;
            _alarmRegistry = alarmRegistry;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Alarm>>> GetAllAlarms(CancellationToken cancellationToken)
        {
            try { return Ok(await _alarmRegistry.GetAllAlarmsAsync(cancellationToken)); }
            catch (Exception ex) { return StatusCode(500, new { message = "Error retrieving alarms", error = ex.Message }); }
        }

        [HttpGet("events")]
        public async Task<ActionResult<IEnumerable<AlarmEvent>>> GetAllAlarmEvents(CancellationToken cancellationToken)
        {
            try { return Ok(await _alarmRegistry.GetAllAlarmEventsAsync(cancellationToken)); }
            catch (Exception ex) { return StatusCode(500, new { message = "Error retrieving alarm events", error = ex.Message }); }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Alarm>> GetAlarmById(int id, CancellationToken cancellationToken)
        {
            var alarm = await _alarmRegistry.GetAlarmByIdAsync(id, cancellationToken);
            if (alarm == null) return NotFound(new { message = "Alarm not found" });
            return Ok(alarm);
        }

        [HttpPost]
        public async Task<ActionResult<Alarm>> CreateAlarm([FromBody] Alarm alarm)
        {
            if (alarm == null) return BadRequest(new { message = "Alarm data is required" });
            _repository.Add(alarm);
            if (!await _repository.SaveAll()) return BadRequest(new { message = "Failed to create alarm" });
            _alarmRegistry.NotifyAlarmCreated(alarm);
            return CreatedAtAction(nameof(GetAlarmById), new { id = alarm.Id }, alarm);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<Alarm>> UpdateAlarm(int id, [FromBody] Alarm body)
        {
            if (body == null) return BadRequest(new { message = "Alarm data is required" });
            var existing = await _repository.GetAlarmById(id);
            if (existing == null) return NotFound(new { message = "Alarm not found" });
            if (body.Id != 0 && body.Id != id)
                return BadRequest(new { message = "Route id and body id must match" });

            existing.Threshold = body.Threshold;
            existing.IsGreaterThan = body.IsGreaterThan;
            existing.Severity = body.Severity;
            if (body.PanelId > 0)
                existing.PanelId = body.PanelId;

            if (!await _repository.SaveAll()) return BadRequest(new { message = "Failed to update alarm" });
            _alarmRegistry.NotifyAlarmUpdated(existing);
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteAlarm(int id)
        {
            var alarm = await _repository.GetAlarmById(id);
            if (alarm == null) return NotFound(new { message = "Alarm not found" });
            _repository.Delete(alarm);
            if (!await _repository.SaveAll()) return BadRequest(new { message = "Failed to delete alarm" });
            _alarmRegistry.NotifyAlarmDeleted(id);
            return NoContent();
        }
    }
}
