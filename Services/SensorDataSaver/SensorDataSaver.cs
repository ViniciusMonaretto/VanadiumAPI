using MQTTnet;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Collections.Concurrent;
using Models;
using Data.Sqlite;
using Data.Mongo;

namespace Services
{
    public class SensorDataSaver : BackgroundService, ISensorDataSaver
    {
        private readonly IMqttService _mqttService;
        private readonly ConcurrentDictionary<int, PanelReading> _lastPanelReadings 
            = new();
        private readonly ILogger<SensorDataSaver> _logger;
        private readonly IServiceProvider _provider;

        public SensorDataSaver(IMqttService mqttService, ILogger<SensorDataSaver> logger, IServiceProvider provider)
        {
            _mqttService = mqttService;
            _mqttService.SensorDataMessageReceived += SensorDataReceived;
            _logger = logger;
            _provider = provider;
        }
        public Task<bool> SubscribeAsync(string topic)
        {
            return Task.FromResult(true);
        }

        private static async Task WaitUntilNextFullMinute(CancellationToken stoppingToken)
        {
            var now = DateTime.Now;
            var nextMinute = now.AddMinutes(1);
            var aligned = new DateTime(
                nextMinute.Year, nextMinute.Month, nextMinute.Day,
                nextMinute.Hour, nextMinute.Minute, 0);

            var delay = aligned - now;

            await Task.Delay(delay, stoppingToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _provider.CreateScope();
            // 1. Wait until the next round minute
            await WaitUntilNextFullMinute(stoppingToken);

            // 2. Now run every 1 minute aligned with the clock
            var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

            do 
            {
                var panelReadingRepo = scope.ServiceProvider.GetRequiredService<IPanelReadingRepository>();
                var panelReadings = _lastPanelReadings.Values.ToList();
                if(panelReadings.Count > 0)
                {
                    await panelReadingRepo.AddAsync(panelReadings);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        async void SensorDataReceived(object sender, SensorDataMessageModel e)
        {
            if(e.GatewayData == null)
            {
                _logger.LogError($"Invalid gateway data: {e.GatewayId}");
                return;
            }

            using var scope = _provider.CreateScope();

            var panelInfoRepo = scope.ServiceProvider.GetRequiredService<IPanelInfoRepository>();
            var panels = (await panelInfoRepo
                                    .GetAllPanels(x => x.GatewayId == e.GatewayId))
                                    .ToDictionary(p => p.Id, p => p);

            for(var i = 0; i < e.GatewayData.Sensors.Count; i++)
            {
                var sensor =  e.GatewayData.Sensors[i];
                if(!panels.TryGetValue(i, out var panel))
                {
                    _logger.LogError($"Panel not found: {i}");
                    continue;
                }
                var panelReading = new PanelReading
                {
                    PanelId = panel.Id,
                    ReadingTime = e.GatewayData.Timestamp,
                };

                _lastPanelReadings.AddOrUpdate( panel.Id, panelReading, (key, existing) => panelReading);
            }
        }
    }
}
