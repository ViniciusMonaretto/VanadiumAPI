using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using API.Hubs;
using Shared.Models;
using System.Text.Json;
using System.Text;
using API.Services;

namespace HubConnectorServer
{
    public class RabbitMQConsumerService : BackgroundService
    {
        private readonly ILogger<RabbitMQConsumerService> _logger;
        private readonly IPanelBroadcastService _broadcastService;
        private readonly IConnection _rabbitMqConnection;
        private readonly IModel _rabbitMqChannel;
        private readonly string _queueName;

        public RabbitMQConsumerService(
            IOptions<RabbitMQOptions> rabbitMqOptions,
            ILogger<RabbitMQConsumerService> logger,
            IPanelBroadcastService panelBroadcastService)
        {
            _logger = logger;
            _broadcastService = panelBroadcastService;

            var rabbitMq = rabbitMqOptions.Value;
            _queueName = rabbitMq.QueueName ?? "signalr-consumer";

            // Configure RabbitMQ connection
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

            // Declare exchange
            _rabbitMqChannel.ExchangeDeclare(
                exchange: "sensor-data",
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);

            // Declare queue
            _rabbitMqChannel.QueueDeclare(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false);

            // Bind to sensor-data (all gateways)
            _rabbitMqChannel.QueueBind(
                queue: _queueName,
                exchange: "sensor-data",
                routingKey: "#");

            // Declare and bind to panel-change exchange if different
            var panelChangeExchange = "panel-change";
            _rabbitMqChannel.ExchangeDeclare(
                exchange: panelChangeExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);

            _rabbitMqChannel.QueueBind(
                queue: _queueName,
                exchange: panelChangeExchange,
                routingKey: "#");
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RabbitMQConsumerService starting...");

            _ = Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

            return Task.CompletedTask;
        }

        private async Task ConsumeLoop(CancellationToken stoppingToken)
{
    try
    {
        var consumer = new EventingBasicConsumer(_rabbitMqChannel);
        consumer.Received += async (model, ea) => await HandleMessageAsync(ea, stoppingToken);

        _rabbitMqChannel.BasicConsume(
            queue: _queueName,
            autoAck: false,
            consumer: consumer);

        _logger.LogInformation("RabbitMQConsumerService subscribed to exchanges: sensor-data, panel-change");

        await WaitForCancellationAsync(stoppingToken);
    }
    catch (OperationCanceledException)
    {
        // Expected on shutdown
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "RabbitMQ error. Retrying in 5 seconds...");
        await Task.Delay(5000, stoppingToken);
    }

    _logger.LogInformation("RabbitMQConsumerService stopped");
}

private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken stoppingToken)
{
    try
    {
        var body = ea.Body.ToArray();
        var messageText = Encoding.UTF8.GetString(body);

        var processed = ea.Exchange switch
        {
            "sensor-data" => await ProcessSensorDataAsync(messageText, stoppingToken),
            "panel-change" => await ProcessPanelChangeAsync(messageText, stoppingToken),
            _ => false
        };

        if (processed)
        {
            _rabbitMqChannel.BasicAck(ea.DeliveryTag, false);
        }
        else
        {
            _logger.LogWarning("Failed to process message from exchange: {Exchange}", ea.Exchange);
            _rabbitMqChannel.BasicNack(ea.DeliveryTag, false, false);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing RabbitMQ message from exchange: {Exchange}", ea.Exchange);
        _rabbitMqChannel.BasicNack(ea.DeliveryTag, false, true); // Requeue on error
    }
}

private async Task<bool> ProcessSensorDataAsync(string messageText, CancellationToken stoppingToken)
{
    var sensorData = JsonSerializer.Deserialize<SensorDataMessageModel>(messageText);
    if (sensorData == null)
    {
        _logger.LogWarning("Failed to deserialize sensor data message");
        return false;
    }

    await _broadcastService.BroadcastSensorData(sensorData);
    return true;
}

private async Task<bool> ProcessPanelChangeAsync(string messageText, CancellationToken stoppingToken)
{
    var panelChange = JsonSerializer.Deserialize<PanelChangeMessage>(messageText);
    if (panelChange == null)
    {
        _logger.LogWarning("Failed to deserialize panel change message");
        return false;
    }

    await _broadcastService.BroadcastPanelChange(panelChange);
    return true;
}

private static async Task WaitForCancellationAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await Task.Delay(1000, stoppingToken);
    }
}

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _rabbitMqChannel?.Close();
            _rabbitMqConnection?.Close();
            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _rabbitMqChannel?.Dispose();
            _rabbitMqConnection?.Dispose();
            base.Dispose();
        }
    }
}
