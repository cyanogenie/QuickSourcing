#nullable enable
using System.Text;
using System.Text.Json;

namespace MyM365Agent1.Services
{
    public interface IExternalApiService
    {
        Task<string> PostAsync(string endpoint, object data, Dictionary<string, string>? headers = null);
    }

    public class ExternalApiService : IExternalApiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ExternalApiService> _logger;

        public ExternalApiService(HttpClient httpClient, IConfiguration configuration, ILogger<ExternalApiService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string> PostAsync(string endpoint, object data, Dictionary<string, string>? headers = null)
        {
            try
            {
                // Get bearer token from configuration
                var bearerToken = _configuration["ExternalApi:BearerToken"];
                var baseUrl = _configuration["ExternalApi:BaseUrl"];

                if (string.IsNullOrEmpty(bearerToken))
                {
                    throw new InvalidOperationException("Bearer token is not configured.");
                }

                if (string.IsNullOrEmpty(baseUrl))
                {
                    throw new InvalidOperationException("Base URL is not configured.");
                }

                // Construct full URL
                var fullUrl = $"{baseUrl.TrimEnd('/')}/{endpoint.TrimStart('/')}";

                // Create request message
                var request = new HttpRequestMessage(HttpMethod.Post, fullUrl);

                // Add authorization header
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

                // Add additional headers if provided
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                }

                // Serialize data to JSON
                var jsonContent = JsonSerializer.Serialize(data);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogInformation($"Making POST request to: {fullUrl}");

                // Send request
                var response = await _httpClient.SendAsync(request);

                // Read response content
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Request successful. Status: {response.StatusCode}");
                    return responseContent;
                }
                else
                {
                    _logger.LogError($"Request failed. Status: {response.StatusCode}, Content: {responseContent}");
                    throw new HttpRequestException($"Request failed with status {response.StatusCode}: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error making POST request to {endpoint}");
                throw;
            }
        }
    }
}
