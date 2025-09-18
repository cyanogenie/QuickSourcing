using Microsoft.Bot.Builder;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Action;
using MyM365Agent1.Model;
using MyM365Agent1.Services;
using System.Text.Json;

namespace MyM365Agent1.Actions
{
    /// <summary>
    /// Action to handle the submission of the project form adaptive card
    /// </summary>
    public class SubmitProjectFormAction
    {
        private readonly IGraphQLService _graphQLService;
        private readonly ILogger<SubmitProjectFormAction> _logger;

        public SubmitProjectFormAction(IGraphQLService graphQLService, ILogger<SubmitProjectFormAction> logger)
        {
            _graphQLService = graphQLService;
            _logger = logger;
        }

        [Action("submitProjectForm")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext, 
            [ActionTurnState] AppState state, 
            [ActionParameters] Dictionary<string, object> parameters)
        {
            try
            {
                _logger.LogInformation("🎯 SubmitProjectFormAction CALLED - Processing project form submission");
                Console.WriteLine("🎯 SubmitProjectFormAction CALLED - Processing project form submission");
                
                _logger.LogInformation("Raw parameters received: {FormData}", JsonSerializer.Serialize(parameters));
                Console.WriteLine($"Raw parameters received: {JsonSerializer.Serialize(parameters)}");

                // Check if data comes from adaptive card submission
                var actualFormData = parameters;
                
                // Sometimes adaptive card data comes wrapped in different ways
                if (parameters.ContainsKey("data"))
                {
                    if (parameters["data"] is JsonElement dataElement)
                    {
                        actualFormData = JsonSerializer.Deserialize<Dictionary<string, object>>(dataElement.GetRawText()) ?? parameters;
                        _logger.LogInformation("Found data wrapped in 'data' property: {FormData}", JsonSerializer.Serialize(actualFormData));
                        Console.WriteLine($"Found data wrapped in 'data' property: {JsonSerializer.Serialize(actualFormData)}");
                    }
                    else if (parameters["data"] is Dictionary<string, object> dataDict)
                    {
                        actualFormData = dataDict;
                        _logger.LogInformation("Found data as dictionary in 'data' property: {FormData}", JsonSerializer.Serialize(actualFormData));
                        Console.WriteLine($"Found data as dictionary in 'data' property: {JsonSerializer.Serialize(actualFormData)}");
                    }
                }

                // Extract form data
                var projectDetails = ExtractFormData(actualFormData);
                
                _logger.LogInformation("Extracted project details: Title='{Title}', Description='{Description}', Email='{Email}', Budget={Budget}", 
                    projectDetails.ProjectTitle, projectDetails.ProjectDescription, projectDetails.EmailId, projectDetails.ApproxTotalBudget);
                Console.WriteLine($"Extracted project details: Title='{projectDetails.ProjectTitle}', Description='{projectDetails.ProjectDescription}', Email='{projectDetails.EmailId}', Budget={projectDetails.ApproxTotalBudget}");

                // Validate required fields
                var validationResult = ValidateProjectDetails(projectDetails);
                if (!validationResult.IsValid)
                {
                    await turnContext.SendActivityAsync($"❌ **Validation Error**\n\n{validationResult.ErrorMessage}");
                    return validationResult.ErrorMessage;
                }

                // Log the extracted details
                _logger.LogInformation("Extracted form details - Title: '{Title}', Description: '{Description}', Email: '{Email}', Budget: {Budget}, StartDate: {StartDate}, EndDate: {EndDate}",
                    projectDetails.ProjectTitle, projectDetails.ProjectDescription, projectDetails.EmailId, 
                    projectDetails.ApproxTotalBudget, projectDetails.StartDate, projectDetails.EndDate);

                // Create engagement ID based on current timestamp
                var engagementId = DateTime.UtcNow.ToString("yyyyMMddHHmm");

                // Set budget to a minimum if user provided a small amount
                var budget = projectDetails.ApproxTotalBudget < 1000 ? 1000 : projectDetails.ApproxTotalBudget;

                // Parse dates with proper time components
                var startDate = projectDetails.StartDate?.Date ?? DateTime.UtcNow.Date.AddDays(1);
                var endDate = projectDetails.EndDate?.Date.AddHours(23).AddMinutes(59) ?? DateTime.UtcNow.Date.AddDays(7).AddHours(23).AddMinutes(59);

                // Create the sourcing project using GraphQL
                var response = await _graphQLService.CreateSourcingProjectAsync(
                    projectDetails.ProjectTitle,
                    projectDetails.ProjectDescription,
                    startDate,
                    endDate,
                    engagementId,
                    budget,
                    projectDetails.EmailId
                );

                // Parse the response to extract project details
                _logger.LogInformation("Raw GraphQL response: {Response}", response);
                
                var responseJson = JsonDocument.Parse(response);
                
                // Log the structure of the response for debugging
                _logger.LogInformation("Response root element properties: {Properties}", 
                    string.Join(", ", responseJson.RootElement.EnumerateObject().Select(p => p.Name)));
                
                // Check for errors in the response first
                if (responseJson.RootElement.TryGetProperty("errors", out var errors))
                {
                    _logger.LogError("GraphQL response contains errors: {Errors}", errors.ToString());
                    await turnContext.SendActivityAsync("❌ The sourcing project creation failed due to validation errors. Please check your input and try again.");
                    return "Failed to create sourcing project - validation errors";
                }
                
                if (responseJson.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind != JsonValueKind.Null &&
                    data.TryGetProperty("createProject", out var createProject) &&
                    createProject.ValueKind != JsonValueKind.Null)
                {
                    // Extract project ID (can be number or string)
                    var projectId = "";
                    if (createProject.TryGetProperty("projectId", out var projectIdElement))
                    {
                        projectId = projectIdElement.ValueKind == JsonValueKind.Number 
                            ? projectIdElement.GetInt32().ToString() 
                            : projectIdElement.GetString() ?? "";
                    }

                    // Extract project status
                    var projectStatus = "";
                    if (createProject.TryGetProperty("projectStatus", out var statusElement))
                    {
                        projectStatus = statusElement.GetString() ?? "";
                    }

                    // Extract returned engagement ID (for verification)
                    var returnedEngagementId = "";
                    if (createProject.TryGetProperty("engagementId", out var engagementIdElement))
                    {
                        returnedEngagementId = engagementIdElement.GetString() ?? "";
                    }

                    if (!string.IsNullOrEmpty(projectId))
                    {
                        // Update state
                        state.User.EmailId = projectDetails.EmailId;
                        state.User.ProjectId = projectId;
                        state.User.EngagementId = !string.IsNullOrEmpty(returnedEngagementId) ? returnedEngagementId : engagementId;
                        state.User.CurrentStep = WorkflowStep.PROJECT_CREATED;

                        await turnContext.SendActivityAsync($"✅ **Sourcing project created successfully!**\n\n" +
                            $"📋 **Project Details:**\n" +
                            $"• **Project ID:** {projectId}\n" +
                            $"• **Status:** {projectStatus}\n" +
                            $"• **Title:** {projectDetails.ProjectTitle}\n" +
                            $"• **Description:** {projectDetails.ProjectDescription}\n" +
                            $"• **Start Date:** {startDate:yyyy-MM-dd}\n" +
                            $"• **End Date:** {endDate:yyyy-MM-dd}\n" +
                            $"• **Budget:** ${budget:N0} USD\n" +
                            $"• **Engagement ID:** {state.User.EngagementId}\n\n" +
                            $"🎯 **Next Step:** Please provide your project milestones and deliverables.");

                        return $"Sourcing project created successfully with ID: {projectId}";
                    }
                    else
                    {
                        _logger.LogError("Project ID not found in response: {Response}", response);
                        await turnContext.SendActivityAsync("❌ Failed to create the sourcing project - no project ID returned. Please try again or contact support.");
                        return "Failed to create sourcing project - no project ID";
                    }
                }
                else
                {
                    // Check if data is null or createProject is null
                    var hasData = responseJson.RootElement.TryGetProperty("data", out var dataCheck);
                    var dataIsNull = hasData && dataCheck.ValueKind == JsonValueKind.Null;
                    
                    _logger.LogError("Failed to parse project creation response. HasData: {HasData}, DataIsNull: {DataIsNull}, Response: {Response}", 
                        hasData, dataIsNull, response);
                    
                    if (dataIsNull)
                    {
                        await turnContext.SendActivityAsync("❌ The project creation request was processed but returned no data. This might indicate a validation issue with the project details. Please verify your input and try again.");
                        return "Failed to create sourcing project - null data returned";
                    }
                    else
                    {
                        await turnContext.SendActivityAsync("❌ Failed to create the sourcing project due to an unexpected response format. Please try again or contact support.");
                        return "Failed to create sourcing project - unexpected response format";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing project form submission");
                state.User.LastError = ex.Message;
                await turnContext.SendActivityAsync("❌ An error occurred while creating your sourcing project. Please try again.");
                return "Error processing project form submission";
            }
        }

        /// <summary>
        /// Extract project details from the adaptive card form data
        /// </summary>
        private ProjectDetails ExtractFormData(Dictionary<string, object> parameters)
        {
            var details = new ProjectDetails();

            // Extract project title
            if (parameters.TryGetValue("projectTitle", out var titleObj) && titleObj != null)
            {
                details.ProjectTitle = titleObj.ToString()?.Trim() ?? "";
            }

            // Extract description
            if (parameters.TryGetValue("description", out var descObj) && descObj != null)
            {
                details.ProjectDescription = descObj.ToString()?.Trim() ?? "";
            }

            // Extract email
            if (parameters.TryGetValue("email", out var emailObj) && emailObj != null)
            {
                details.EmailId = emailObj.ToString()?.Trim() ?? "";
            }

            // Extract budget
            if (parameters.TryGetValue("budget", out var budgetObj) && budgetObj != null)
            {
                if (decimal.TryParse(budgetObj.ToString(), out var budget))
                {
                    details.ApproxTotalBudget = budget;
                }
            }

            // Extract start date
            if (parameters.TryGetValue("startDate", out var startDateObj) && startDateObj != null)
            {
                if (DateTime.TryParse(startDateObj.ToString(), out var startDate))
                {
                    details.StartDate = startDate;
                }
            }

            // Extract end date
            if (parameters.TryGetValue("endDate", out var endDateObj) && endDateObj != null)
            {
                if (DateTime.TryParse(endDateObj.ToString(), out var endDate))
                {
                    details.EndDate = endDate;
                }
            }

            return details;
        }

        /// <summary>
        /// Validate the extracted project details
        /// </summary>
        private ValidationResult ValidateProjectDetails(ProjectDetails details)
        {
            var errors = new List<string>();

            // Validate project title
            if (string.IsNullOrWhiteSpace(details.ProjectTitle))
            {
                errors.Add("• Project title is required");
            }

            // Validate description
            if (string.IsNullOrWhiteSpace(details.ProjectDescription))
            {
                errors.Add("• Project description is required");
            }

            // Validate email
            if (string.IsNullOrWhiteSpace(details.EmailId))
            {
                errors.Add("• Email address is required");
            }
            else if (!IsValidEmail(details.EmailId))
            {
                errors.Add("• Please provide a valid email address");
            }

            // Validate budget
            if (details.ApproxTotalBudget <= 0)
            {
                errors.Add("• Budget must be greater than 0");
            }

            // Validate dates
            if (details.StartDate == null)
            {
                errors.Add("• Start date is required");
            }

            if (details.EndDate == null)
            {
                errors.Add("• End date is required");
            }

            if (details.StartDate != null && details.EndDate != null && details.EndDate <= details.StartDate)
            {
                errors.Add("• End date must be after the start date");
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                ErrorMessage = errors.Count > 0 ? string.Join("\n", errors) : ""
            };
        }

        /// <summary>
        /// Validate email format
        /// </summary>
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validation result for project details
        /// </summary>
        private class ValidationResult
        {
            public bool IsValid { get; set; }
            public string ErrorMessage { get; set; } = "";
        }
    }
}