using Microsoft.AspNetCore.Mvc;

namespace SensorInfoServer.Controllers
{
    [ApiController]
    [Route("sensorInfo")]
    public class SensorInfoController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                status = "ok",
                message = "Sensor info endpoint"
            });
        }
    }
}