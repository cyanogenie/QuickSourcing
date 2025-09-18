#nullable enable
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace MyM365Agent1.Services
{
    /// <summary>
    /// Interface for secure API service using Azure Managed Identity
    /// </summary>
    public interface ISecureApiServiceHttpClient
    {
        Task<string> SubmitUserDetailsAsync(string email);
        Task<string> ConfirmOrderAsync(string orderId);
        Task<string> CancelOrderAsync(string orderId);
        Task<string> PostAsync(string endpoint, object data, Dictionary<string, string>? headers = null);
    }

    /// <summary>
    /// Secure API service that uses DefaultAzureCredential for authentication
    /// following Azure security best practices
    /// </summary>
    public class SecureApiServiceHttpClient : ISecureApiServiceHttpClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SecureApiServiceHttpClient> _logger;
        private static readonly DefaultAzureCredential _credential = new();
        private readonly string[] _apiScopes;
        private readonly string _baseUrl;

        public SecureApiServiceHttpClient(
            IHttpClientFactory httpClientFactory, 
            IConfiguration configuration, 
            ILogger<SecureApiServiceHttpClient> logger)
        {
            _httpClient = httpClientFactory.CreateClient("SecureApiService");
            _configuration = configuration;
            _logger = logger;

            // Configure API scopes and base URL from configuration
            var apiScope = _configuration["SecureApi:Scope"] ?? "https://api.quicksourcing.com/.default";
            _apiScopes = new[] { apiScope };
            _baseUrl = _configuration["SecureApi:BaseUrl"] ?? 
                      throw new InvalidOperationException("SecureApi:BaseUrl is not configured.");

            _logger.LogInformation("SecureApiServiceHttpClient initialized with scope: {Scope}", apiScope);
        }

        /// <summary>
        /// Submit user details and receive an order ID
        /// </summary>
        public async Task<string> SubmitUserDetailsAsync(string email)
        {
            try
            {
                if (string.IsNullOrEmpty(email))
                {
                    throw new ArgumentException("Email cannot be null or empty", nameof(email));
                }

                _logger.LogInformation("Submitting user details for email: {Email}", email);

                var payload = new
                {
                    email = email,
                    timestamp = DateTime.UtcNow,
                    source = "M365Agent"
                };

                var response = await PostAsync("user/submit-details", payload);
                
                // Parse response to extract order ID
                using var document = JsonDocument.Parse(response);
                var orderId = document.RootElement.GetProperty("orderId").GetString();
                
                if (string.IsNullOrEmpty(orderId))
                {
                    throw new InvalidOperationException("Order ID not found in API response");
                }

                _logger.LogInformation("User details submitted successfully. Order ID: {OrderId}", orderId);
                return orderId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting user details for email: {Email}", email);
                throw;
            }
        }

        /// <summary>
        /// Confirm an order by order ID
        /// </summary>
        public async Task<string> ConfirmOrderAsync(string orderId)
        {
            try
            {
                if (string.IsNullOrEmpty(orderId))
                {
                    throw new ArgumentException("Order ID cannot be null or empty", nameof(orderId));
                }

                _logger.LogInformation("Confirming order: {OrderId}", orderId);

                var payload = new
                {
                    orderId = orderId,
                    action = "confirm",
                    timestamp = DateTime.UtcNow
                };

                var response = await PostAsync($"orders/{orderId}/confirm", payload);
                _logger.LogInformation("Order confirmed successfully: {OrderId}", orderId);
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming order: {OrderId}", orderId);
                throw;
            }
        }

        /// <summary>
        /// Cancel an order by order ID
        /// </summary>
        public async Task<string> CancelOrderAsync(string orderId)
        {
            try
            {
                if (string.IsNullOrEmpty(orderId))
                {
                    throw new ArgumentException("Order ID cannot be null or empty", nameof(orderId));
                }

                _logger.LogInformation("Cancelling order: {OrderId}", orderId);

                var payload = new
                {
                    orderId = orderId,
                    action = "cancel",
                    timestamp = DateTime.UtcNow
                };

                var response = await PostAsync($"orders/{orderId}/cancel", payload);
                _logger.LogInformation("Order cancelled successfully: {OrderId}", orderId);
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling order: {OrderId}", orderId);
                throw;
            }
        }

        /// <summary>
        /// Generic POST method that handles authentication via Managed Identity
        /// </summary>
        public async Task<string> PostAsync(string endpoint, object data, Dictionary<string, string>? headers = null)
        {
            try
            {
                // Get access token using DefaultAzureCredential
                var token = await GetAccessTokenAsync();
                
                // Construct full URL
                var fullUrl = $"{_baseUrl.TrimEnd('/')}/{endpoint.TrimStart('/')}";

                // Create request message
                var request = new HttpRequestMessage(HttpMethod.Post, fullUrl);

                // Add authorization header with managed identity token
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                // Add additional headers if provided
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                }

                // Serialize data to JSON
                var jsonContent = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogDebug("Making POST request to: {FullUrl}", fullUrl);

                // Send request with retry logic
                var response = await SendWithRetryAsync(request);

                // Read response content
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Request successful. Status: {StatusCode}", response.StatusCode);
                    return responseContent;
                }
                else
                {
                    _logger.LogError("Request failed. Status: {StatusCode}, Content: {Content}", 
                        response.StatusCode, responseContent);
                    throw new HttpRequestException($"Request failed with status {response.StatusCode}: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error making POST request to {Endpoint}", endpoint);
                throw;
            }
        }

        /// <summary>
        /// Acquire access token using DefaultAzureCredential
        /// </summary>
        private async Task<string> GetAccessTokenAsync()
        {
            try
            {
                var tokenRequestContext = new TokenRequestContext(_apiScopes);
                var accessToken = await _credential.GetTokenAsync(tokenRequestContext);
                
                _logger.LogDebug("Successfully acquired access token. Expires at: {ExpiresOn}", accessToken.ExpiresOn);
                return accessToken.Token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to acquire access token using DefaultAzureCredential");
                throw new InvalidOperationException("Failed to acquire access token. Ensure Managed Identity is properly configured.", ex);
            }
        }

        /// <summary>
        /// Send HTTP request with exponential backoff retry logic
        /// </summary>
        private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request)
        {
            const int maxRetries = 3;
            const int baseDelayMs = 1000;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Clone the request for retry attempts
                    var requestClone = await CloneHttpRequestMessageAsync(request);
                    var response = await _httpClient.SendAsync(requestClone);

                    // If success or non-retryable error, return response
                    if (response.IsSuccessStatusCode || !IsRetryableStatusCode(response.StatusCode))
                    {
                        return response;
                    }

                    // If this is the last attempt, return the response (which will be handled as an error)
                    if (attempt == maxRetries)
                    {
                        return response;
                    }

                    // Calculate delay with exponential backoff
                    var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt));
                    _logger.LogWarning("Request failed with status {StatusCode}. Retrying in {Delay}ms. Attempt {Attempt}/{MaxRetries}", 
                        response.StatusCode, delay.TotalMilliseconds, attempt + 1, maxRetries);
                    
                    await Task.Delay(delay);
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt));
                    _logger.LogWarning(ex, "Request failed with exception. Retrying in {Delay}ms. Attempt {Attempt}/{MaxRetries}", 
                        delay.TotalMilliseconds, attempt + 1, maxRetries);
                    
                    await Task.Delay(delay);
                }
            }

            // This should never be reached, but added for safety
            throw new InvalidOperationException("Retry logic failed unexpectedly");
        }

        /// <summary>
        /// Check if HTTP status code is retryable
        /// </summary>
        private static bool IsRetryableStatusCode(System.Net.HttpStatusCode statusCode)
        {
            return statusCode == System.Net.HttpStatusCode.TooManyRequests ||
                   statusCode == System.Net.HttpStatusCode.InternalServerError ||
                   statusCode == System.Net.HttpStatusCode.BadGateway ||
                   statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                   statusCode == System.Net.HttpStatusCode.GatewayTimeout;
        }

        /// <summary>
        /// Clone HttpRequestMessage for retry attempts
        /// </summary>
        private static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage original)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri);

            // Copy headers
            foreach (var header in original.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Copy content if present
            if (original.Content != null)
            {
                var content = await original.Content.ReadAsStringAsync();
                clone.Content = new StringContent(content, Encoding.UTF8, original.Content.Headers.ContentType?.MediaType ?? "application/json");
                
                // Copy content headers
                foreach (var header in original.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }
}