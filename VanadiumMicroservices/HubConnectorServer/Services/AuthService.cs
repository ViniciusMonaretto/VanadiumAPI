using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HubConnectorServer.DTO;

namespace API.Services
{
    public class AuthService : IAuthService
    {
        private readonly HttpClient _sensorInfoHttpClient;
        private readonly ILogger<AuthService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public AuthService(HttpClient sensorInfoHttpClient, ILogger<AuthService> logger)
        {
            _sensorInfoHttpClient = sensorInfoHttpClient;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };
        }

        public async Task<AuthResponseDto?> LoginAsync(LoginDto loginDto)
        {
            try
            {
                var response = await _sensorInfoHttpClient.PostAsJsonAsync("api/auth/login", loginDto, _jsonOptions);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Unauthorized login attempt for email: {Email}", loginDto.Email);
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Login failed with status code: {StatusCode}", response.StatusCode);
                    return null;
                }

                var result = await response.Content.ReadFromJsonAsync<AuthResponseDto>(_jsonOptions);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login to SensorInfoServer");
                throw;
            }
        }
    }
}

