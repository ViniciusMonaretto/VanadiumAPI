using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Models;

namespace API.Services
{
    public class PanelReadingService : IPanelReadingService
    {
        private readonly HttpClient _sensorDataHttpClient;
        private readonly ILogger<PanelReadingService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public PanelReadingService(HttpClient sensorDataHttpClient, ILogger<PanelReadingService> logger)
        {
            _sensorDataHttpClient = sensorDataHttpClient;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };
        }

        public async Task<IEnumerable<PanelReading>> GetPanelReadingsAsync(int panelId, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                var queryParams = new List<string>();
                if (startDate.HasValue)
                {
                    queryParams.Add($"startDate={Uri.EscapeDataString(startDate.Value.ToString("o"))}");
                }
                if (endDate.HasValue)
                {
                    queryParams.Add($"endDate={Uri.EscapeDataString(endDate.Value.ToString("o"))}");
                }

                var queryString = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
                var url = $"api/PanelReadings/{panelId}{queryString}";

                var response = await _sensorDataHttpClient.GetAsync(url);
                
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<IEnumerable<PanelReading>>(_jsonOptions);
                return result ?? Enumerable.Empty<PanelReading>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching panel readings for panel {PanelId} from SensorDataServer", panelId);
                throw;
            }
        }

        public async Task<Dictionary<int, List<PanelReading>>> GetMultiplePanelReadingsAsync(IEnumerable<int> panelIds, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                var request = new MultiplePanelReadingsRequest
                {
                    PanelIds = panelIds,
                    StartDate = startDate,
                    EndDate = endDate
                };

                var response = await _sensorDataHttpClient.PostAsJsonAsync("api/PanelReadings/multiple", request, _jsonOptions);
                
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<Dictionary<int, List<PanelReading>>>(_jsonOptions);
                return result ?? new Dictionary<int, List<PanelReading>>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching multiple panel readings from SensorDataServer");
                throw;
            }
        }
    }

    public class MultiplePanelReadingsRequest
    {
        public IEnumerable<int> PanelIds { get; set; } = Enumerable.Empty<int>();
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}

