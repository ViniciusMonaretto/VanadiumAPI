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
            var recentReadings = _lastPanelReadings.Values
                                                   .Where(x => x.ReadingTime >= cutoffTime).ToList();
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
            var saveNowReadings = new List<PanelReading>();

            using (var scope = _provider.CreateScope())
            {
                var panelRepo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
                for (var i = 0; i < message.GatewayData.Sensors.Count; i++)
                {
                    var (reading, saveNow) = await ProcessSensorReadingAsync(message.GatewayId, i, message.GatewayData.Sensors[i], timestamp, panelRepo);
                    if (reading != null && saveNow)
                        saveNowReadings.Add(reading);
                }
            }

            if (saveNowReadings.Count > 0)
            {
                var toInsert = saveNowReadings;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _provider.CreateScope();
                        var repo = scope.ServiceProvider.GetRequiredService<IPanelReadingRepository>();
                        await repo.AddAsync(toInsert);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to persist {Count} flow sensor readings", toInsert.Count);
                    }
                });
            }
        }

        private bool ShouldSaveNow(Panel panel)
        {
            return panel.Type == PanelType.Flow;
        }

        private async Task<(PanelReading? reading, bool saveNow)> ProcessSensorReadingAsync(string gatewayId, int sensorIndex, SensorData sensor, DateTime timestamp, IPanelInfoRepository panelRepo)
        {
            var indexStr = sensorIndex.ToString();
            var panel = await panelRepo.GetPanelByGatewayAndIndexAsync(gatewayId, indexStr);
            if (panel == null)
            {
                _logger.LogDebug("Panel not found in database: GatewayId={GatewayId}, Index={Index}", gatewayId, indexStr);
                return (null, false);
            }

            var panelReading = new PanelReading
            {
                PanelId = panel.Id,
                ReadingTime = timestamp,
                Value = sensor.Value,
                Active = sensor.Active,
            };

            if (ShouldSaveNow(panel))
            {
                _lastPanelReadings.AddOrUpdate(panel.Id, panelReading, (_, _) => panelReading);
                return (panelReading, true);
            }

            _lastPanelReadings.AddOrUpdate(panel.Id, panelReading, (_, _) => panelReading);
            return (panelReading, false);
        }

        public PanelReading? GetLastPanelReading(int panelId) =>
            _lastPanelReadings.TryGetValue(panelId, out var reading) ? reading : null;

        public void AddPanel(Panel panel)
        {
            var key = $"{panel.GatewayId}-{panel.Index}";
            _panels.AddOrUpdate(key, panel, (_, _) => panel);
        }

        public void UpdatePanel(Panel panel)
        {
            var key = $"{panel.GatewayId}-{panel.Index}";
            _panels.AddOrUpdate(key, panel, (_, _) => panel);
        }

        public void RemovePanel(int panelId, string gatewayId, string index)
        {
            var key = $"{gatewayId}-{index}";
            _panels.TryRemove(key, out _);
            _lastPanelReadings.TryRemove(panelId, out _);
        }
    }
}
