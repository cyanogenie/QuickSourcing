using System.Text;
using System.Text.Json;

namespace MyM365Agent1.Services
{
    public interface IGraphQLService
    {
        Task<string> CreateSourcingProjectAsync(string projectTitle, string engagementDescription, 
            DateTime engagementStartDate, DateTime engagementEndDate, string engagementId, 
            decimal approxTotalBudget, string requestorAlias);
        Task<string> UpsertMilestonesAsync(string engagementId, List<ProjectMilestone> milestones);
        Task<string> UpsertProjectSuppliersAsync(string projectId, List<SelectedSupplier> suppliers);
        Task<string> PublishProjectAsync(int projectId, string projectTitle, string supplierResponseStartDate, 
            string supplierResponseDueBy, string awardTargetDate);
        Task<string> ExecuteQueryAsync(string query, object? variables = null, string? customBaseUrl = null);
    }

    public class ProjectMilestone
    {
        public string Title { get; set; } = string.Empty;
        public DateTime DeliveryDate { get; set; }
    }

    public class SelectedSupplier
    {
        public string VendorName { get; set; } = string.Empty;
        public string VendorNumber { get; set; } = string.Empty;
        public string CompanyCode { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public double Rating { get; set; }
        public string Status { get; set; } = string.Empty;
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

        public async Task<string> CreateSourcingProjectAsync(string projectTitle, string engagementDescription, 
            DateTime engagementStartDate, DateTime engagementEndDate, string engagementId, 
            decimal approxTotalBudget, string requestorAlias)
        {
            var query = @"
                mutation {
                  createProject(
                    input: {
                      projectTitle: """ + projectTitle + @"""
                      engagementDescription: """ + engagementDescription + @"""
                      projectTypeId: 1
                      engagementStartDate: """ + engagementStartDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + @"""
                      engagementEndDate: """ + engagementEndDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + @"""
                      engagementId: """ + engagementId + @"""
                      engagementCompanyCode: ""1010""
                      engagementCategory: ""Consulting Services""
                      engagementCapability: ""IT Consulting""
                      currencyCode: ""USD""
                      approxTotalBudget: " + approxTotalBudget + @"
                      requestorAlias: """ + requestorAlias + @"""
                      businessSponsorAlias: """ + requestorAlias + @"""
                    }
                  ) {
                    projectId
                    projectStatus
                    engagementId
                  }
                }";

            var createProjectUrl = _configuration["GraphQLEndpoints:ProjectManagement:BaseUrl"] + "?tenant=QuickSourcing?action=createProject&tenantName=QuickSourcing";
            return await ExecuteQueryAsync(query, null, createProjectUrl);
        }

        public async Task<string> UpsertMilestonesAsync(string engagementId, List<ProjectMilestone> milestones)
        {
            var milestonesJson = string.Join("\n\t\t\t\t", milestones.Select(m => 
                $@"{{ title: ""{m.Title}"", deliveryDate: ""{m.DeliveryDate:yyyy-MM-ddTHH:mm:ss.fffZ}"" }}"));

            var query = $@"
                mutation {{
		          upsertEngagementInfo(
		          input: {{input: {{
			          engagementId: ""{engagementId}""
		         categoryId: ""18""
		         categoryName: ""Consulting Services""
		         sourcingDescription: """"
			          engagementMilestones: [
				        {milestonesJson}
		        ] }}}}
		          ) {{
		          engagementMilestoneResponse {{    engagementId

			        engagementMilestones {{
			          title

			          deliveryDate
			        }}
		          }}}}
		        }}";

            var milestonesUrl = _configuration["GraphQLEndpoints:RFXGeneration:BaseUrl"] + "?tenant=QuickSourcing?action=saveProjectDeliverablesAsync&tenantName=QuickSourcing";
            return await ExecuteQueryAsync(query, null, milestonesUrl);
        }

        public async Task<string> UpsertProjectSuppliersAsync(string projectId, List<SelectedSupplier> suppliers)
        {
            // Use the format that matches SelectSuppliersAction.cs
            var suppliersList = string.Join(",", suppliers.Select(s => 
                $"{{vendorNumber:\"{s.VendorNumber}\",companyCode:\"{s.CompanyCode}\"}}"));

            var query = $@"
                mutation {{
                    upsertProjectSuppliers(
                        input: {{
                            projectId: {projectId},
                            suppliersList: [{suppliersList}]
                        }}
                    ) {{
                        projectId
                    }}
                }}";

            var suppliersUrl = _configuration["GraphQLEndpoints:ProjectManagement:BaseUrl"] + "?tenant=QuickSourcing&action=upsertProjectSuppliers&tenantName=QuickSourcing";
            return await ExecuteQueryAsync(query, null, suppliersUrl);
        }

        public async Task<string> PublishProjectAsync(int projectId, string projectTitle, string supplierResponseStartDate, 
            string supplierResponseDueBy, string awardTargetDate)
        {
            var query = $@"
                mutation {{
                  publishProject(
                    input: {{
                      projectId: {projectId}
                      projectTitle: ""{projectTitle}""
                      supplierResponseStartDate: ""{supplierResponseStartDate}""
                      supplierResponseDueBy: ""{supplierResponseDueBy}""
                      awardTargetDate: ""{awardTargetDate}""
                      isReminderMailsEnabled: true
                      additionalMsContacts: null
                    }}
                  ) {{
                    projectId
                    projectStatus
                  }}
                }}";

            var publishUrl = _configuration["GraphQLEndpoints:ProjectManagement:BaseUrl"] + "?tenant=QuickSourcing&action=publishProject&tenantName=QuickSourcing";
            return await ExecuteQueryAsync(query, null, publishUrl);
        }

        public async Task<string> ExecuteQueryAsync(string query, object? variables = null, string? customBaseUrl = null)
        {
            try
            {
                // Get configuration - use custom URL if provided, otherwise fallback to config
                var baseUrl = customBaseUrl ?? _configuration["ExternalApi:BaseUrl"];
                string bearerToken;

                // Determine which bearer token to use based on the URL
                if (!string.IsNullOrEmpty(customBaseUrl))
                {
                    bearerToken = GetBearerTokenForUrl(customBaseUrl);
                }
                else
                {
                    bearerToken = _configuration["ExternalApi:BearerToken"] ?? string.Empty;
                }

                if (string.IsNullOrEmpty(bearerToken))
                {
                    throw new InvalidOperationException($"Bearer token is not configured for URL: {baseUrl}");
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

        /// <summary>
        /// Gets the appropriate bearer token for a given URL based on configuration
        /// </summary>
        private string GetBearerTokenForUrl(string url)
        {
            // Use ProjectManagement bearer token for both ProjectManagement and RFXGeneration endpoints
            return _configuration["GraphQLEndpoints:ProjectManagement:BearerToken"] ?? string.Empty;
        }
    }
}
