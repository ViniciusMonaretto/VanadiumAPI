using Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace SensorInfoServer.Controllers
{
    [ApiController]
    [Route("alive")]
    public class AliveController : ControllerBase
    {
        public AliveController()
        {
        }

        // GET: api/alarms
        [HttpGet]
        public async Task<ActionResult<string>> GetAlive()
        {
            return Ok("Alive");
        }
    }
}