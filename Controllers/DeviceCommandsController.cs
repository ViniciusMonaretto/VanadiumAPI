using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models.Mqtt;
using VanadiumAPI.Services.DeviceCommands;

namespace VanadiumAPI.Controllers
{
    [ApiController]
    [Route("api/devices/{deviceId}")]
    [Authorize]
    public class DeviceCommandsController : ControllerBase
    {
        private readonly IDeviceCommandService _commandService;

        public DeviceCommandsController(IDeviceCommandService commandService)
        {
            _commandService = commandService;
        }

        [HttpPost("commands/reboot")]
        public async Task<ActionResult> Reboot(string deviceId, CancellationToken cancellationToken)
        {
            var (ok, error) = await _commandService.RebootAsync(deviceId, cancellationToken);
            return ok ? NoContent() : StatusCode(502, new { message = error });
        }

        [HttpPost("commands/sensors")]
        public async Task<ActionResult<GetSensorsData>> GetSensors(string deviceId, CancellationToken cancellationToken)
        {
            var (data, error) = await _commandService.GetSensorsAsync(deviceId, cancellationToken);
            return data != null ? Ok(data) : StatusCode(502, new { message = error });
        }

        [HttpGet("info")]
        public async Task<ActionResult<GetDeviceInfoData>> GetDeviceInfo(string deviceId, CancellationToken cancellationToken)
        {
            var (data, error) = await _commandService.GetDeviceInfoAsync(deviceId, cancellationToken);
            return data != null ? Ok(data) : StatusCode(502, new { message = error });
        }

        [HttpPut("sensors/config")]
        public async Task<ActionResult> SetSensorConfigBulk(string deviceId, [FromBody] List<SetSensorConfigParams> sensors, CancellationToken cancellationToken)
        {
            var (ok, errors, error) = await _commandService.SetSensorConfigBulkAsync(deviceId, sensors, cancellationToken);
            if (ok) return NoContent();
            if (errors != null) return UnprocessableEntity(new { errors });
            return StatusCode(502, new { message = error });
        }
    }
}
