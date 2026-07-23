using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VanadiumAPI.Mqtt;

namespace VanadiumAPI.Services
{
    /// <summary>
    /// Periodically sweeps gateways for missed heartbeats/telemetry and flips them offline
    /// once they've been silent for longer than MqttOptions.HeartbeatTimeoutSeconds.
    /// </summary>
    public class GatewayHeartbeatMonitorService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

        private readonly IGatewayServerService _gatewayServer;
        private readonly ILogger<GatewayHeartbeatMonitorService> _logger;
        private readonly TimeSpan _heartbeatTimeout;

        public GatewayHeartbeatMonitorService(
            IGatewayServerService gatewayServer,
            IOptions<MqttOptions> mqttOptions,
            ILogger<GatewayHeartbeatMonitorService> logger)
        {
            _gatewayServer = gatewayServer;
            _logger = logger;
            _heartbeatTimeout = TimeSpan.FromSeconds(mqttOptions.Value.HeartbeatTimeoutSeconds);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _gatewayServer.CheckGatewayHeartbeatTimeoutsAsync(_heartbeatTimeout);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking gateway heartbeat timeouts");
                }
            }
        }
    }
}
