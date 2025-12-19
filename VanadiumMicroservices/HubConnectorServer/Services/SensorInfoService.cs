using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Models;

namespace API.Services
{
    public class SensorInfoService : ISensorInfoService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<SensorInfoService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public SensorInfoService(HttpClient httpClient, ILogger<SensorInfoService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };
        }

        public async Task<IEnumerable<Panel>> GetAllPanelsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/sensorInfo");
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<IEnumerable<Panel>>(_jsonOptions);
                return result ?? Enumerable.Empty<Panel>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all panels from SensorInfoServer");
                throw;
            }
        }

        public async Task<Panel?> GetPanelByIdAsync(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/sensorInfo/{id}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<Panel>(_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching panel {PanelId} from SensorInfoServer", id);
                throw;
            }
        }

        public async Task<IEnumerable<Alarm>> GetAllAlarmsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/alarms");
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<IEnumerable<Alarm>>(_jsonOptions);
                return result ?? Enumerable.Empty<Alarm>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all alarms from SensorInfoServer");
                throw;
            }
        }

        public async Task<Alarm?> GetAlarmByIdAsync(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/alarms/{id}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<Alarm>(_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching alarm {AlarmId} from SensorInfoServer", id);
                throw;
            }
        }

        public async Task<IEnumerable<AlarmEvent>> GetAllAlarmEventsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/alarms/events");
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<IEnumerable<AlarmEvent>>(_jsonOptions);
                return result ?? Enumerable.Empty<AlarmEvent>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all alarm events from SensorInfoServer");
                throw;
            }
        }

        public async Task<IEnumerable<Group>> GetAllGroupsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/groups");
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<IEnumerable<Group>>(_jsonOptions);
                return result ?? Enumerable.Empty<Group>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all groups from SensorInfoServer");
                throw;
            }
        }

        public async Task<Group?> GetGroupByIdAsync(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/groups/{id}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<Group>(_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching group {GroupId} from SensorInfoServer", id);
                throw;
            }
        }
    }
}

