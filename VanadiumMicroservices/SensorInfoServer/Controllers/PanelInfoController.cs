using Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Models;
using Confluent.Kafka;
using System.Text.Json;

namespace SensorInfoServer.Controllers
{
    [ApiController]
    [Route("api/sensorInfo")]
    public class PanelInfoController : ControllerBase
    {

        private readonly IPanelInfoRepository _repository;
        private readonly IProducer<string, string> _kafkaProducer;
        private readonly ILogger<PanelInfoController> _logger;
        public PanelInfoController(IPanelInfoRepository panelInfoRepository, IOptions<KafkaOptions> kafkaOptions, ILogger<PanelInfoController> logger)
        {
            _repository = panelInfoRepository;
            _logger = logger;
            // KAFKA Setup
            var kafka = kafkaOptions.Value;

            var config = new ProducerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                ClientId = kafka.ClientId ?? "mqtt-kafka-bridge",
                // Configurações de performance
                Acks = Acks.Leader, // Ou Acks.All para maior confiabilidade
                LingerMs = 10, // Aguarda 10ms para batching
                BatchSize = 32768, // 32KB batch
                CompressionType = CompressionType.Snappy,
                // Retry
                MessageSendMaxRetries = 3,
                RetryBackoffMs = 100
            };

            // Adicionar autenticação se necessário
            if (!string.IsNullOrEmpty(kafka.SaslUsername))
            {
                config.SecurityProtocol = SecurityProtocol.SaslSsl;
                config.SaslMechanism = SaslMechanism.Plain;
                config.SaslUsername = kafka.SaslUsername;
                config.SaslPassword = kafka.SaslPassword;
            }

            _kafkaProducer = new ProducerBuilder<string, string>(config)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Reason}", e.Reason))
                .Build();
        }

        private async Task SendPanelChangeMessage(PanelChangeAction action, Panel panel)
        {
            var panelInfo = new PanelChangeMessage
            {
                Action = action,
                Panel = panel
            };
            var payload = JsonSerializer.Serialize(panelInfo);
            var message = new Message<string, string>
            {
                Key = "1",
                Value = payload,
                Timestamp = new Timestamp(DateTime.UtcNow)
            };

            // Envio assíncrono (mais rápido)
            var task = _kafkaProducer.ProduceAsync("panel-change", message);
            await task;
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
    }
}