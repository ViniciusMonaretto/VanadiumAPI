using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Data.Mongo;
using Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace VanadiumAPI.SensorDataSaver
{
    public class SensorDataSaver : BackgroundService, ISensorDataSaver
    {
        private readonly Channel<SensorDataMessageModel> _channel = Channel.CreateUnbounded<SensorDataMessageModel>(new UnboundedChannelOptions { SingleReader = true });
        private readonly ConcurrentDictionary<int, PanelReading> _lastPanelReadings = new();
        private readonly ConcurrentDictionary<string, Panel> _panels = new();
        private readonly ILogger<SensorDataSaver> _logger;
        private readonly IServiceProvider _provider;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        public SensorDataSaver(
            ILogger<SensorDataSaver> logger,
            IServiceProvider provider)
        {
            _logger = logger;
            _provider = provider;
        }

        public void PushSensorData(SensorDataMessageModel message)
        {
            _channel.Writer.TryWrite(message);
        }

        public Task<bool> SubscribeAsync(string topic) => Task.FromResult(true);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _ = Task.Run(() => LoadPanelsAsync(stoppingToken), stoppingToken);

            var consumerTask = Task.Run(() => ConsumeChannelAsync(stoppingToken), stoppingToken);
            try
            {
                await WaitUntilNextFullMinute(stoppingToken);
                await RunPeriodicPersistenceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            finally
            {
                await consumerTask;
            }
        }

        private static async Task WaitUntilNextFullMinute(CancellationToken stoppingToken)
        {
            var now = DateTime.Now;
            var nextMinute = now.AddMinutes(1);
            var aligned = new DateTime(nextMinute.Year, nextMinute.Month, nextMinute.Day, nextMinute.Hour, nextMinute.Minute, 0);
            await Task.Delay(aligned - now, stoppingToken);
        }

        private async Task RunPeriodicPersistenceAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do
            {
                await PersistRecentPanelReadingsAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task PersistRecentPanelReadingsAsync(CancellationToken stoppingToken)
        {
            var cutoffTime = DateTime.Now.AddSeconds(-90);
            var recentReadings = _lastPanelReadings.Values.Where(x => x.ReadingTime >= cutoffTime).ToList();
            if (recentReadings.Count == 0) return;

            using var scope = _provider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPanelReadingRepository>();
            try
            {
                await repo.AddAsync(recentReadings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist {Count} panel readings", recentReadings.Count);
            }
        }

        private async Task LoadPanelsAsync(CancellationToken stoppingToken)
        {
            for (var i = 0; i < 10; i++)
            {
                if (stoppingToken.IsCancellationRequested) return;
                try
                {
                    using var scope = _provider.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                    var panels = await repo.GetAllPanels();
                    if (panels != null)
                    {
                        foreach (var panel in panels)
                        {
                            var key = $"{panel.GatewayId}-{panel.Index}";
                            _panels.AddOrUpdate(key, panel, (_, _) => panel);
                        }
                        _logger.LogInformation("Loaded {Count} panels for SensorDataSaver", _panels.Count);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading panels");
                }
                await Task.Delay(2000, stoppingToken);
            }
        }

        private async Task ConsumeChannelAsync(CancellationToken stoppingToken)
        {
            await foreach (var message in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessSensorDataMessage(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing sensor data message");
                }
            }
        }

        private async Task ProcessSensorDataMessage(SensorDataMessageModel message)
        {
            if (message.GatewayData?.Sensors == null)
            {
                _logger.LogError("Invalid gateway data: {GatewayId}", message.GatewayId);
                return;
            }

            var timestamp = message.GatewayData.Timestamp;
            for (var i = 0; i < message.GatewayData.Sensors.Count; i++)
                ProcessSensorReading(message.GatewayId, i, message.GatewayData.Sensors[i], timestamp);

            await Task.CompletedTask;
        }

        private bool ProcessSensorReading(string gatewayId, int sensorIndex, SensorData sensor, DateTime timestamp)
        {
            var sensorKey = $"{gatewayId}-{sensorIndex}";
            if (!_panels.TryGetValue(sensorKey, out var panel))
            {
                _logger.LogDebug("Panel not found: {Index} for GatewayId: {GatewayId}", sensorIndex, gatewayId);
                return false;
            }

            var panelReading = new PanelReading
            {
                PanelId = panel.Id,
                ReadingTime = timestamp,
                Value = sensor.Value,
            };
            _lastPanelReadings.AddOrUpdate(panel.Id, panelReading, (_, _) => panelReading);
            return true;
        }
    }
}
