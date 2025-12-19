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

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("KafkaConsumerService starting...");

            _ = Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

            return Task.CompletedTask;
        }

        private async Task ConsumeLoop(CancellationToken stoppingToken)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = "localhost:9092",
                GroupId = "signalr-consumer",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var consumer = new ConsumerBuilder<string, string>(config).Build();

                    consumer.Subscribe(new[]
                    {
                        "sensor-data",
                        "panel-change"
                    });

                    _logger.LogInformation("KafkaConsumerService subscribed to topics");

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var result = consumer.Consume(stoppingToken);

                        switch (result.Topic)
                        {
                            case "sensor-data":
                                var sensorData = JsonSerializer.Deserialize<SensorDataMessageModel>(result.Message.Value);
                                if (sensorData != null)
                                {
                                    await _hubContext.Clients.All.SendAsync(
                                        "SensorDataReceived",
                                        sensorData,
                                        stoppingToken);
                                }
                                break;

                            case "panel-change":
                                var panelChange = JsonSerializer.Deserialize<PanelChangeMessage>(result.Message.Value);
                                if (panelChange != null)
                                {
                                    await _hubContext.Clients.All.SendAsync(
                                        "KafkaMessageReceived",
                                        panelChange,
                                        stoppingToken);
                                }
                                break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Kafka error. Retrying in 5 seconds...");
                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("KafkaConsumerService stopped");
        }
    }
}
