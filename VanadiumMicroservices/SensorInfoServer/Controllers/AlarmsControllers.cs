using Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace SensorInfoServer.Controllers
{
     [ApiController]
    [Route("api/alarms")]
    public class AlarmsController : ControllerBase
    {
        private readonly IPanelInfoRepository _repository;

        public AlarmsController(IPanelInfoRepository repository)
        {
            _repository = repository;
        }

        // GET: api/alarms
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Alarm>>> GetAllAlarms()
        {
            try
            {
                var alarms = await _repository.GetAllAlarms();
                return Ok(alarms);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving alarms", error = ex.Message });
            }
        }

        // GET: api/alarms/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Alarm>> GetAlarmById(int id)
        {
            try
            {
                var alarm = await _repository.GetAlarmById(id);
                
                if (alarm == null)
                    return NotFound(new { message = $"Alarm with id {id} not found" });

                return Ok(alarm);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving alarm", error = ex.Message });
            }
        }

        // GET: api/alarms/events
        [HttpGet("events")]
        public async Task<ActionResult<IEnumerable<AlarmEvent>>> GetAllAlarmEvents()
        {
            try
            {
                var alarmEvents = await _repository.GetAllAlarmEvents();
                return Ok(alarmEvents);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving alarm events", error = ex.Message });
            }
        }

        // POST: api/alarms
        [HttpPost]
        public async Task<ActionResult<Alarm>> CreateAlarm([FromBody] Alarm alarm)
        {
            try
            {
                if (alarm == null)
                    return BadRequest(new { message = "Alarm data is required" });

                _repository.Add(alarm);
                
                if (await _repository.SaveAll())
                    return CreatedAtAction(nameof(GetAlarmById), new { id = alarm.Id }, alarm);

                return BadRequest(new { message = "Failed to create alarm" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error creating alarm", error = ex.Message });
            }
        }

        // DELETE: api/alarms/5
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteAlarm(int id)
        {
            try
            {
                var alarm = await _repository.GetAlarmById(id);
                
                if (alarm == null)
                    return NotFound(new { message = $"Alarm with id {id} not found" });

                _repository.Delete(alarm);
                
                if (await _repository.SaveAll())
                    return NoContent();

                return BadRequest(new { message = "Failed to delete alarm" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error deleting alarm", error = ex.Message });
            }
        }
    }
}