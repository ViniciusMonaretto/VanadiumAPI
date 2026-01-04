using Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Models;
using RabbitMQ.Client;
using System.Text.Json;
using System.Text;

namespace SensorInfoServer.Controllers
{
    [ApiController]
    [Route("api/sensorInfo")]
    public class PanelInfoController : ControllerBase, IDisposable
    {

        private readonly IPanelInfoRepository _repository;
        private readonly IConnection _rabbitMqConnection;
        private readonly IModel _rabbitMqChannel;
        private readonly string _panelChangeExchange;
        private readonly ILogger<PanelInfoController> _logger;
        
        public PanelInfoController(IPanelInfoRepository panelInfoRepository, IOptions<RabbitMQOptions> rabbitMqOptions, ILogger<PanelInfoController> logger)
        {
            _repository = panelInfoRepository;
            _logger = logger;
            
            // RABBITMQ Setup
            var rabbitMq = rabbitMqOptions.Value;

            var factory = new ConnectionFactory
            {
                HostName = rabbitMq.HostName,
                Port = rabbitMq.Port,
                VirtualHost = rabbitMq.VirtualHost ?? "/",
                UserName = rabbitMq.UserName ?? "guest",
                Password = rabbitMq.Password ?? "guest",
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _logger.LogInformation(
                "Attempting to connect to RabbitMQ at {HostName}:{Port} with user {UserName}",
                rabbitMq.HostName,
                rabbitMq.Port,
                rabbitMq.UserName ?? "guest");

            try
            {
                _rabbitMqConnection = factory.CreateConnection();
                _rabbitMqChannel = _rabbitMqConnection.CreateModel();
                _logger.LogInformation("Successfully connected to RabbitMQ");
            }
            catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException ex)
            {
                _logger.LogError(ex,
                    "Failed to connect to RabbitMQ broker at {HostName}:{Port}. " +
                    "Please ensure RabbitMQ is running and accessible.",
                    rabbitMq.HostName,
                    rabbitMq.Port);
                throw;
            }
            catch (RabbitMQ.Client.Exceptions.AuthenticationFailureException ex)
            {
                _logger.LogError(ex,
                    "RabbitMQ authentication failed for user '{UserName}'. " +
                    "Please verify the username and password in appsettings.json",
                    rabbitMq.UserName ?? "guest");
                throw;
            }
            
            _panelChangeExchange = "panel-change";

            // Declare panel-change exchange
            _rabbitMqChannel.ExchangeDeclare(
                exchange: _panelChangeExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);
        }

        private async Task SendPanelChangeMessage(PanelChangeAction action, Panel panel)
        {
            try
            {
                var panelInfo = new PanelChangeMessage
                {
                    Action = action,
                    Panel = panel
                };
                var payload = JsonSerializer.Serialize(panelInfo);
                var body = Encoding.UTF8.GetBytes(payload);

                var properties = _rabbitMqChannel.CreateBasicProperties();
                properties.Persistent = true;
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                properties.MessageId = Guid.NewGuid().ToString();

                // Use action as routing key (e.g., "panel-change.create", "panel-change.update", "panel-change.delete")
                var routingKey = $"panel-change.{action.ToString().ToLower()}";

                _rabbitMqChannel.BasicPublish(
                    exchange: _panelChangeExchange,
                    routingKey: routingKey,
                    basicProperties: properties,
                    body: body);

                _logger.LogDebug("Published panel change message: {Action} for panel {PanelId}", action, panel.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending panel change message to RabbitMQ");
            }
        }
        
        // GET: api/panels
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

        // GET: api/panels/5
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

        // POST: api/panels
        [HttpPost]
        public async Task<ActionResult<Panel>> CreatePanel([FromBody] Panel panel)
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
                _logger.LogError(ex, "Error creating panel");
                return StatusCode(500, new { message = "Error creating panel", error = ex.Message });
            }
        }

        // PUT: api/panels/5
        [HttpPut("{id}")]
        public async Task<ActionResult> UpdatePanel(int id, [FromBody] Panel panel)
        {
            try
            {
                if (panel == null)
                    return BadRequest(new { message = "Panel data is required" });

                var existingPanel = await _repository.GetPanelById(id);
                
                if (existingPanel == null)
                    return NotFound(new { message = $"Panel with id {id} not found" });

                // Update properties (adjust based on your Panel model)
                // Example: existingPanel.Name = panel.Name;

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

        // DELETE: api/panels/5
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

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _rabbitMqChannel?.Dispose();
                _rabbitMqConnection?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}