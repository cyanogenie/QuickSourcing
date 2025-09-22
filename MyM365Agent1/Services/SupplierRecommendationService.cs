using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyM365Agent1.Services
{
    public class SupplierRecommendationService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _bearerToken;
        private readonly ILogger<SupplierRecommendationService> _logger;

        public SupplierRecommendationService(HttpClient httpClient, IConfiguration configuration, ILogger<SupplierRecommendationService> logger)
        {
            _httpClient = httpClient;
            _baseUrl = configuration["SupplierRecommendationApi:BaseUrl"] ?? throw new ArgumentNullException("SupplierRecommendationApi:BaseUrl");
            _bearerToken = configuration["SupplierRecommendationApi:BearerToken"] ?? throw new ArgumentNullException("SupplierRecommendationApi:BearerToken");
            _logger = logger;

            // Set up default headers
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", _bearerToken);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public async Task<string> GetSupplierRecommendationsAsync(string[] inputList)
        {
            try
            {
                // Create the correct payload format expected by the API
                var payload = new 
                { 
                    modelsToRun = new[] { "SupplierRecommendation" },
                    inputList = new[] { inputList }, // Wrap inputList in another array
                    extras = new { is_sspa = "False" }
                };
                var jsonContent = JsonSerializer.Serialize(payload);
                
                _logger.LogInformation("🔍 Sending supplier recommendation request to: {Url}", _baseUrl);
                _logger.LogInformation("🔍 Request payload: {Payload}", jsonContent);
                
                using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(_baseUrl, content);
                
                // Capture response content before throwing exception for better error handling
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}). Response content: {responseContent}");
                }
                
                return responseContent;
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"HTTP request failed: {ex.Message}", ex);
            }
            catch (JsonException ex)
            {
                throw new Exception($"JSON serialization failed: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Supplier recommendation request failed: {ex.Message}", ex);
            }
        }
    }
}