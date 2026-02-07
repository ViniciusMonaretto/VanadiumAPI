using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Models;
using HubConnectorServer.DTO;

namespace API.Services
{
    public class SensorInfoService : ISensorInfoService
    {
        private readonly HttpClient _sensorInfoHttpClient;
        private readonly ILogger<SensorInfoService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public SensorInfoService(HttpClient sensorInfoHttpClient, ILogger<SensorInfoService> logger)
        {
            _sensorInfoHttpClient = sensorInfoHttpClient;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };
        }

        private static bool IsFailureResponse(HttpResponseMessage response, params System.Net.HttpStatusCode[] orStatuses)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return true;
            foreach (var code in orStatuses)
                if (response.StatusCode == code)
                    return true;
            return false;
        }

        public async Task<IEnumerable<Panel>> GetAllPanelsAsync()
        {
            try
            {
                var response = await _sensorInfoHttpClient.GetAsync("api/sensorInfo");
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
                var response = await _sensorInfoHttpClient.GetAsync($"api/sensorInfo/{id}");
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
                var response = await _sensorInfoHttpClient.GetAsync("api/alarms");
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
                var response = await _sensorInfoHttpClient.GetAsync($"api/alarms/{id}");
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
                var response = await _sensorInfoHttpClient.GetAsync("api/alarms/events");
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

        public async Task<IEnumerable<Group>> GetAllGroupsAsync(int? enterpriseId)
        {
            try
            {
                var response = enterpriseId.HasValue ?
                    await _sensorInfoHttpClient.GetAsync($"api/groups/enterprise/{enterpriseId.Value}") :
                    await _sensorInfoHttpClient.GetAsync("api/groups");
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
                var response = await _sensorInfoHttpClient.GetAsync($"api/groups/{id}");
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

        public async Task<IEnumerable<UserInfo>> GetManagedUsersAsync(string token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/users/managed");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _sensorInfoHttpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<IEnumerable<UserInfo>>(_jsonOptions);
                return result ?? Enumerable.Empty<UserInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching managed users from SensorInfoServer");
                throw;
            }
        }

        public async Task<UserInfo?> CreateManagedUserAsync(CreateManagedUserDto dto, string token)
        {
            try
            {
                var body = new { dto.Name, dto.Username, dto.Email, dto.Company, Password = dto.Password, UserType = dto.UserType };
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/users/managed");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = JsonContent.Create(body, options: _jsonOptions);
                var response = await _sensorInfoHttpClient.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<UserInfo>(_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating managed user in SensorInfoServer");
                throw;
            }
        }

        public async Task<bool> DeleteManagedUserAsync(int userId, string token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/users/managed/{userId}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _sensorInfoHttpClient.SendAsync(request);
                if (IsFailureResponse(response))
                    return false;
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting managed user in SensorInfoServer");
                throw;
            }
        }

        public async Task<IEnumerable<Enterprise>> GetUserEnterprisesAsync(int userId, string token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"api/users/{userId}/enterprises");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _sensorInfoHttpClient.SendAsync(request);
                if (IsFailureResponse(response))
                    return Enumerable.Empty<Enterprise>();
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<IEnumerable<Enterprise>>(_jsonOptions);
                return result ?? Enumerable.Empty<Enterprise>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user enterprises from SensorInfoServer");
                throw;
            }
        }

        public async Task<bool> AddUserToEnterpriseAsync(int userId, int enterpriseId, string token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"api/users/{userId}/enterprises/{enterpriseId}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _sensorInfoHttpClient.SendAsync(request);
                if (IsFailureResponse(response, System.Net.HttpStatusCode.Conflict))
                    return false;
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    throw new InvalidOperationException(
                        "A empresa atingiu o número máximo de usuários permitido (MaxUsers).");
                }
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding user to enterprise in SensorInfoServer");
                throw;
            }
        }

        public async Task<bool> RemoveUserFromEnterpriseAsync(int userId, int enterpriseId, string token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/users/{userId}/enterprises/{enterpriseId}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _sensorInfoHttpClient.SendAsync(request);
                if (IsFailureResponse(response))
                    return false;
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing user from enterprise in SensorInfoServer");
                throw;
            }
        }
    }
}

