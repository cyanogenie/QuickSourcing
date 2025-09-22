using Microsoft.Bot.Builder;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Action;
using MyM365Agent1.Model;
using MyM365Agent1.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyM365Agent1.Actions
{
    /// <summary>
    /// Action to create a new sourcing project by collecting project details from the user
    /// </summary>
    public class CreateSourcingProjectAction
    {
        private readonly IGraphQLService _graphQLService;
        private readonly ILogger<CreateSourcingProjectAction> _logger;

        public CreateSourcingProjectAction(IGraphQLService graphQLService, ILogger<CreateSourcingProjectAction> logger)
        {
            _graphQLService = graphQLService;
            _logger = logger;
        }

        [Action("createSourcingProject")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext, 
            [ActionTurnState] AppState state, 
            [ActionParameters] Dictionary<string, object> parameters)
        {
            try
            {
                _logger.LogInformation("CreateSourcingProjectAction triggered");

                // Only proceed if in PROJECT_TO_BE_CREATED state
                if (state.User.CurrentStep != WorkflowStep.PROJECT_TO_BE_CREATED)
                {
                    await turnContext.SendActivityAsync("I can only create a sourcing project when starting a new workflow. Please type 'reset' to start over.");
                    return "Action not available in current workflow step";
                }

                // Extract project details from user input
                var input = parameters.ContainsKey("input") ? parameters["input"]?.ToString() : turnContext.Activity.Text ?? "";
                _logger.LogInformation("Raw input for project creation: {Input}", input);
                _logger.LogInformation("Input length: {Length}, Parameters count: {Count}", input.Length, parameters.Count);
                _logger.LogInformation("All parameters: {Parameters}", string.Join(", ", parameters.Select(kvp => $"{kvp.Key}='{kvp.Value}'")));
                
                var projectDetails = ExtractProjectDetails(input);
                
                // Log extracted details for debugging
                _logger.LogInformation("Extracted project details - Title: '{Title}', Description: '{Description}', Email: '{Email}', Budget: {Budget}", 
                    projectDetails.ProjectTitle, projectDetails.ProjectDescription, projectDetails.EmailId, projectDetails.ApproxTotalBudget);

                // Validate required fields
                if (string.IsNullOrEmpty(projectDetails.ProjectTitle))
                {
                    _logger.LogWarning("Project title validation failed. Extracted title: '{Title}' from input: '{Input}'", 
                        projectDetails.ProjectTitle, input);
                    await turnContext.SendActivityAsync($"I need a project title to create your sourcing project. Please provide the project title.\n\n" +
                        $"**Debug Info:** I extracted the following from your input:\n" +
                        $"• Title: '{projectDetails.ProjectTitle}'\n" +
                        $"• Description: '{projectDetails.ProjectDescription}'\n" +
                        $"• Email: '{projectDetails.EmailId}'\n" +
                        $"• Budget: {projectDetails.ApproxTotalBudget}");
                    return "Missing project title";
                }

                if (string.IsNullOrEmpty(projectDetails.EmailId))
                {
                    _logger.LogWarning("Email validation failed. Extracted email: '{Email}' from input: '{Input}'", 
                        projectDetails.EmailId, input);
                    await turnContext.SendActivityAsync($"I need your email address to create the sourcing project. Please provide your email.\n\n" +
                        $"**Debug Info:** I extracted the following from your input:\n" +
                        $"• Title: '{projectDetails.ProjectTitle}'\n" +
                        $"• Description: '{projectDetails.ProjectDescription}'\n" +
                        $"• Email: '{projectDetails.EmailId}'\n" +
                        $"• Budget: {projectDetails.ApproxTotalBudget}");
                    return "Missing email address";
                }

                if (string.IsNullOrEmpty(projectDetails.ProjectDescription))
                {
                    _logger.LogWarning("Description validation failed. Extracted description: '{Description}' from input: '{Input}'", 
                        projectDetails.ProjectDescription, input);
                    await turnContext.SendActivityAsync($"I need a project description to create your sourcing project. Please describe what you want to source.\n\n" +
                        $"**Debug Info:** I extracted the following from your input:\n" +
                        $"• Title: '{projectDetails.ProjectTitle}'\n" +
                        $"• Description: '{projectDetails.ProjectDescription}'\n" +
                        $"• Email: '{projectDetails.EmailId}'\n" +
                        $"• Budget: {projectDetails.ApproxTotalBudget}");
                    return "Missing project description";
                }

                // Generate engagement ID using current state ID
                var engagementId = state.User.StateId;
                
                // Set default dates if not provided
                var startDate = projectDetails.StartDate ?? DateTime.UtcNow.AddDays(1);
                var endDate = projectDetails.EndDate ?? DateTime.UtcNow.AddDays(30);

                // Set default budget if not provided
                var budget = projectDetails.ApproxTotalBudget > 0 ? projectDetails.ApproxTotalBudget : 1000;

                await turnContext.SendActivityAsync("🔄 Creating your sourcing project...");

                // Call GraphQL service to create the project
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
                        // Prepare parsed data for API response history
                        var parsedData = new Dictionary<string, object>
                        {
                            ["projectId"] = projectId,
                            ["engagementId"] = !string.IsNullOrEmpty(returnedEngagementId) ? returnedEngagementId : engagementId,
                            ["projectStatus"] = projectStatus,
                            ["projectTitle"] = projectDetails.ProjectTitle,
                            ["projectDescription"] = projectDetails.ProjectDescription,
                            ["startDate"] = startDate,
                            ["endDate"] = endDate,
                            ["budget"] = budget
                        };

                        // Update existing state properties (maintain compatibility)
                        state.User.EmailId = projectDetails.EmailId;
                        state.User.ProjectId = projectId;
                        state.User.EngagementId = !string.IsNullOrEmpty(returnedEngagementId) ? returnedEngagementId : engagementId;
                        state.User.CurrentStep = WorkflowStep.PROJECT_CREATED;
                        state.User.LastActivityTime = DateTime.UtcNow;

                        // Store essential data as simple properties to avoid serialization issues
                        state.User.ProjectTitle = projectDetails.ProjectTitle;
                        state.User.ProjectDescription = projectDetails.ProjectDescription;
                        state.User.LastApiResponse = response;

                        // Log state update for debugging
                        _logger.LogInformation("🔄 State updated: CurrentStep={CurrentStep}, ProjectId={ProjectId}, EngagementId={EngagementId}, EmailId={EmailId}",
                            state.User.CurrentStep, state.User.ProjectId, state.User.EngagementId, state.User.EmailId);
                        _logger.LogInformation("📦 Essential project data stored as simple properties");
                        Console.WriteLine($"🔄 State updated: CurrentStep={state.User.CurrentStep}, ProjectId={state.User.ProjectId}, EngagementId={state.User.EngagementId}");
                        Console.WriteLine("📦 API response stored in history for cross-step access");

                        await turnContext.SendActivityAsync($"🎉 **Sourcing Project Created Successfully!**\n\n" +
                            $"┌─────────────────────────────────────────────────────────────┐\n" +
                            $"│                  � **PROJECT OVERVIEW**                    │\n" +
                            $"└─────────────────────────────────────────────────────────────┘\n\n" +
                            $"🆔 **Project ID:** `{projectId}`\n" +
                            $"📈 **Status:** {projectStatus}\n" +
                            $"🏷️ **Title:** {projectDetails.ProjectTitle}\n" +
                            $"📝 **Description:** {projectDetails.ProjectDescription}\n" +
                            $"📅 **Start Date:** `{startDate:yyyy-MM-dd}`\n" +
                            $"📅 **End Date:** `{endDate:yyyy-MM-dd}`\n" +
                            $"💰 **Budget:** `${budget:N0} USD`\n" +
                            $"🔗 **Engagement ID:** `{state.User.EngagementId}`\n\n" +
                            $"─────────────────────────────────────────────────────────────\n\n" +
                            $"🎯 **Next Step:** Please provide your project milestones and deliverables to continue the setup process.");

                        _logger.LogInformation("✅ Project creation completed successfully. State should be persisted automatically.");
                        Console.WriteLine("✅ Project creation completed successfully. State should be persisted automatically.");

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
                _logger.LogError(ex, "Error creating sourcing project");
                state.User.LastError = ex.Message;
                await turnContext.SendActivityAsync("❌ An error occurred while creating your sourcing project. Please try again.");
                return "Error creating sourcing project";
            }
        }

        /// <summary>
        /// Extract project details from user input - handles both JSON and text formats
        /// </summary>
        private ProjectDetails ExtractProjectDetails(string input)
        {
            var details = new ProjectDetails();

            // First try to parse as JSON
            try
            {
                if (input.Trim().StartsWith("{") && input.Trim().EndsWith("}"))
                {
                    var jsonDoc = JsonDocument.Parse(input);
                    var root = jsonDoc.RootElement;

                    // Extract values from JSON - try multiple property name variations
                    
                    // Extract title - try "projectTitle" first, then "title"
                    if (root.TryGetProperty("projectTitle", out var projectTitleElement))
                        details.ProjectTitle = projectTitleElement.GetString() ?? "";
                    else if (root.TryGetProperty("title", out var titleElement))
                        details.ProjectTitle = titleElement.GetString() ?? "";
                    
                    // Extract description
                    if (root.TryGetProperty("description", out var descElement))
                        details.ProjectDescription = descElement.GetString() ?? "";
                    
                    // Extract email
                    if (root.TryGetProperty("email", out var emailElement))
                        details.EmailId = emailElement.GetString() ?? "";
                    
                    // Extract budget - try "approxTotalBudget" first, then "budget"
                    JsonElement budgetElement;
                    var hasBudget = root.TryGetProperty("approxTotalBudget", out budgetElement) || 
                                   root.TryGetProperty("budget", out budgetElement);
                    
                    if (hasBudget)
                    {
                        if (budgetElement.ValueKind == JsonValueKind.Number)
                            details.ApproxTotalBudget = budgetElement.GetDecimal();
                        else if (budgetElement.ValueKind == JsonValueKind.String && 
                                decimal.TryParse(budgetElement.GetString(), out var budgetFromString))
                            details.ApproxTotalBudget = budgetFromString;
                    }
                    
                    // Extract start date - try "engagementStartDate" first, then "startDate"
                    JsonElement startDateElement;
                    var hasStartDate = root.TryGetProperty("engagementStartDate", out startDateElement) || 
                                      root.TryGetProperty("startDate", out startDateElement);
                    
                    if (hasStartDate)
                    {
                        var startDateStr = startDateElement.GetString();
                        if (DateTime.TryParse(startDateStr, out var startDate))
                            details.StartDate = startDate;
                    }
                    
                    // Extract end date - try "engagementEndDate" first, then "endDate"
                    JsonElement endDateElement;
                    var hasEndDate = root.TryGetProperty("engagementEndDate", out endDateElement) || 
                                    root.TryGetProperty("endDate", out endDateElement);
                    
                    if (hasEndDate)
                    {
                        var endDateStr = endDateElement.GetString();
                        if (DateTime.TryParse(endDateStr, out var endDate))
                            details.EndDate = endDate;
                    }

                    // Log the JSON parsing results for debugging
                    _logger.LogInformation("JSON parsing results - ProjectTitle: '{ProjectTitle}', Description: '{Description}', Email: '{Email}', Budget: {Budget}, StartDate: {StartDate}, EndDate: {EndDate}",
                        details.ProjectTitle, details.ProjectDescription, details.EmailId, details.ApproxTotalBudget, details.StartDate, details.EndDate);

                    return details;
                }
            }
            catch (JsonException)
            {
                // If JSON parsing fails, fall back to regex patterns
            }

            // Fallback to regex patterns for non-JSON input
            return ExtractProjectDetailsFromText(input);
        }

        /// <summary>
        /// Extract project details using regex patterns for text-based input
        /// </summary>
        private ProjectDetails ExtractProjectDetailsFromText(string input)
        {
            var details = new ProjectDetails();

            // Extract email using regex
            var emailPattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b";
            var emailMatch = Regex.Match(input, emailPattern, RegexOptions.IgnoreCase);
            if (emailMatch.Success)
            {
                details.EmailId = emailMatch.Value;
            }

            // Extract project title - support various formats with fallbacks
            // Primary patterns: "projectTitle:", "title:", "project title:", values in quotes or without
            var titlePattern = @"(?:project\s*)?title\s*[:\s]+[""']?([^""',\n\r]+?)[""']?(?:\s*[,\n\r]|$)";
            var titleMatch = Regex.Match(input, titlePattern, RegexOptions.IgnoreCase);
            if (titleMatch.Success)
            {
                details.ProjectTitle = titleMatch.Groups[1].Value.Trim().Trim('"', '\'');
            }
            else
            {
                // Fallback: look for any quoted string after 'title' keyword
                var titleFallback = @"title[^""]*[""']([^""']+)[""']";
                var titleFallbackMatch = Regex.Match(input, titleFallback, RegexOptions.IgnoreCase);
                if (titleFallbackMatch.Success)
                {
                    details.ProjectTitle = titleFallbackMatch.Groups[1].Value.Trim();
                }
            }

            // Extract description - support various formats with fallbacks
            // Primary patterns: "Description:", "description:", "project description:", values in quotes or without
            var descPattern = @"(?:project\s*)?description\s*[:\s]+[""']?([^""',\n\r]+?)[""']?(?:\s*[,\n\r]|$)";
            var descMatch = Regex.Match(input, descPattern, RegexOptions.IgnoreCase);
            if (descMatch.Success)
            {
                details.ProjectDescription = descMatch.Groups[1].Value.Trim().Trim('"', '\'');
            }
            else
            {
                // Fallback: look for any quoted string after 'description' keyword
                var descFallback = @"description[^""]*[""']([^""']+)[""']";
                var descFallbackMatch = Regex.Match(input, descFallback, RegexOptions.IgnoreCase);
                if (descFallbackMatch.Success)
                {
                    details.ProjectDescription = descFallbackMatch.Groups[1].Value.Trim();
                }
            }

            // Extract budget - look for various patterns
            // Patterns: "approxTotalBudget: 100", "budget: $100"
            var budgetPattern = @"approx\s*total\s*budget\s*[:\s]+(\d+(?:,\d{3})*(?:\.\d{2})?)|budget\s*[:\s]+\$?(\d+(?:,\d{3})*(?:\.\d{2})?)";
            var budgetMatch = Regex.Match(input, budgetPattern, RegexOptions.IgnoreCase);
            if (budgetMatch.Success)
            {
                var budgetValue = "";
                for (int i = 1; i < budgetMatch.Groups.Count; i++)
                {
                    if (!string.IsNullOrEmpty(budgetMatch.Groups[i].Value))
                    {
                        budgetValue = budgetMatch.Groups[i].Value.Replace(",", "");
                        break;
                    }
                }
                
                if (decimal.TryParse(budgetValue, out var budget))
                {
                    details.ApproxTotalBudget = budget;
                }
            }

            // Extract dates - support ISO format and simple date format
            // Patterns: "2025-10-16T18:30:00.000Z", "2025-10-16"
            var isoDatePattern = @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{3})?Z?)";
            var simpleDatePattern = @"\b(\d{4}-\d{2}-\d{2})\b";
            
            // Try ISO dates first
            var isoDateMatches = Regex.Matches(input, isoDatePattern);
            if (isoDateMatches.Count >= 1 && DateTime.TryParse(isoDateMatches[0].Value, out var isoStartDate))
            {
                details.StartDate = isoStartDate;
            }
            if (isoDateMatches.Count >= 2 && DateTime.TryParse(isoDateMatches[1].Value, out var isoEndDate))
            {
                details.EndDate = isoEndDate;
            }
            
            // If no ISO dates found, try simple dates
            if (details.StartDate == null || details.EndDate == null)
            {
                var simpleDateMatches = Regex.Matches(input, simpleDatePattern);
                if (details.StartDate == null && simpleDateMatches.Count >= 1 && DateTime.TryParse(simpleDateMatches[0].Value, out var simpleStartDate))
                {
                    details.StartDate = simpleStartDate;
                }
                if (details.EndDate == null && simpleDateMatches.Count >= 2 && DateTime.TryParse(simpleDateMatches[1].Value, out var simpleEndDate))
                {
                    details.EndDate = simpleEndDate;
                }
            }

            // Extract email from "email:" pattern if not found by basic email pattern
            if (string.IsNullOrEmpty(details.EmailId))
            {
                var emailFieldPattern = @"email\s*[:\s]+[""']?([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,})[""']?";
                var emailFieldMatch = Regex.Match(input, emailFieldPattern, RegexOptions.IgnoreCase);
                if (emailFieldMatch.Success)
                {
                    details.EmailId = emailFieldMatch.Groups[1].Value.Trim().Trim('"', '\'');
                }
            }

            return details;
        }
    }
}