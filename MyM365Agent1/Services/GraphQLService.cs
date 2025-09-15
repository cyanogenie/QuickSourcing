using System.Text;
using System.Text.Json;

namespace MyM365Agent1.Services
{
    public interface IGraphQLService
    {
        Task<string> GetEventDetailsAsync(int eventId);
        Task<string> ExecuteQueryAsync(string query, object? variables = null);
    }

    public class GraphQLService : IGraphQLService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GraphQLService> _logger;

        public GraphQLService(HttpClient httpClient, IConfiguration configuration, ILogger<GraphQLService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string> GetEventDetailsAsync(int eventId)
        {
            var query = @"
                query($eventId: Int!) {
                    eventDetails(eventId: $eventId) {
                        eventId
                        microsoftSupplierId
                        companyCode
                        country
                        currency
                        programName
                        eventTitle
                        projectType
                        eventStatus
                        lastAction
                        isSystemProcfessing
                        systemProcessingAction
                        supplierName
                        supplierLegalName
                        supplierUvpName
                        inCountrySupplierContact {
                            firstName
                            lastName
                            email
                        }
                        supplierType
                        supplierCcContactEmail
                        supplierAdminContacts
                        owner
                        eventRequestor
                        msContactEmail
                        msCcContactEmail
                        approver
                    }
                }";

            var variables = new { eventId = eventId };
            return await ExecuteQueryAsync(query, variables);
        }

        public async Task<string> ExecuteQueryAsync(string query, object? variables = null)
        {
            try
            {
                // Get configuration
                var baseUrl = _configuration["ExternalApi:BaseUrl"];
                var bearerToken = _configuration["ExternalApi:BearerToken"];

                if (string.IsNullOrEmpty(bearerToken))
                {
                    throw new InvalidOperationException("Bearer token is not configured.");
                }

                if (string.IsNullOrEmpty(baseUrl))
                {
                    throw new InvalidOperationException("GraphQL endpoint URL is not configured.");
                }

                // Create the GraphQL request payload
                var requestPayload = new
                {
                    query = query,
                    variables = variables
                };

                // Create request message
                var request = new HttpRequestMessage(HttpMethod.Post, baseUrl);

                // Add authorization header - check if Bearer is already included
                if (bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken.Substring(7));
                }
                else
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
                }

                // Add content type header
                request.Headers.Add("Accept", "application/json");

                // Serialize the GraphQL request to JSON
                var jsonContent = JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogInformation($"Making GraphQL request to: {baseUrl}");
                _logger.LogInformation($"Query: {query}");
                if (variables != null)
                {
                    _logger.LogInformation($"Variables: {JsonSerializer.Serialize(variables)}");
                }

                // Send request
                var response = await _httpClient.SendAsync(request);

                // Read response content
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"GraphQL request successful. Status: {response.StatusCode}");
                    return responseContent;
                }
                else
                {
                    _logger.LogError($"GraphQL request failed. Status: {response.StatusCode}, Content: {responseContent}");
                    throw new HttpRequestException($"GraphQL request failed with status {response.StatusCode}: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing GraphQL query");
                throw;
            }
        }
    }
}
