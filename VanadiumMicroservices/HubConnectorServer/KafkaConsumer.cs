using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using API.Hubs;
using Shared.Models;
using System.Text.Json;
namespace HubConnectorServer
{
    public class KafkaConsumerService : BackgroundService
    {
        private readonly ILogger<KafkaConsumerService> _logger;
        private readonly IHubContext<PanelReadingsHub> _hubContext;

        public KafkaConsumerService(
            ILogger<KafkaConsumerService> logger,
            IHubContext<PanelReadingsHub> hubContext)
        {
            _logger = logger;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = "localhost:9092",
                GroupId = "signalr-consumer",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };

            using var consumer = new ConsumerBuilder<string, string>(config).Build();

            consumer.Subscribe(new[]
            {
                "sensor-data",
                "panel-change"
            });

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = consumer.Consume(stoppingToken);

                    switch (result.Topic)
                    {
                        case "sensor-data":
                            var sensorData = JsonSerializer.Deserialize<SensorDataMessageModel>(result.Message.Value);
                            if (sensorData == null)
                            {
                                _logger.LogError("Failed to deserialize sensor data: {Value}", result.Message.Value);
                                continue;
                            }
                            await _hubContext.Clients.All.SendAsync(
                                  "SensorDataReceived",
                                  sensorData,
                                  stoppingToken
                              );
                            break;
                        case "panel-change":
                            var panelChange = JsonSerializer.Deserialize<PanelChangeMessage>(result.Message.Value);
                            if (panelChange == null)
                            {
                                _logger.LogError("Failed to deserialize panel change: {Value}", result.Message.Value);
                                continue;
                            }
                            await _hubContext.Clients.All.SendAsync(
                                "KafkaMessageReceived",
                                panelChange,
                                stoppingToken
                            );
                            break;
                    }

                    await _hubContext.Clients.All.SendAsync(
                        "KafkaMessageReceived",
                        new
                        {
                            Topic = result.Topic,
                            Key = result.Message.Key,
                            Value = result.Message.Value,
                            Timestamp = result.Message.Timestamp.UtcDateTime
                        },
                        stoppingToken
                    );
                }
            }
            catch (OperationCanceledException)
            {
                consumer.Close();
            }
        }
    }
}