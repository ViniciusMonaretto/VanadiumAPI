using Microsoft.AspNetCore.Mvc;

namespace VanadiumAPI.Controllers
{
    [ApiController]
    [Route("alive")]
    public class AliveController : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<string>> GetAlive()
        {
            return Ok("Alive");
        }
    }
}
