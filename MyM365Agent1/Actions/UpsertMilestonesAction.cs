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
    /// Action to add or update project milestones for an existing sourcing project
    /// </summary>
    public class UpsertMilestonesAction
    {
        private readonly IGraphQLService _graphQLService;
        private readonly ILogger<UpsertMilestonesAction> _logger;

        public UpsertMilestonesAction(IGraphQLService graphQLService, ILogger<UpsertMilestonesAction> logger)
        {
            _graphQLService = graphQLService;
            _logger = logger;
        }

        [Action("upsertMilestones")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext, 
            [ActionTurnState] AppState state, 
            [ActionParameters] Dictionary<string, object> parameters)
        {
            try
            {
                _logger.LogInformation("UpsertMilestonesAction triggered");

                // Only proceed if in PROJECT_CREATED state
                if (state.User.CurrentStep != WorkflowStep.PROJECT_CREATED)
                {
                    await turnContext.SendActivityAsync("I can only add milestones after a sourcing project has been created. Please create a project first.");
                    return "Action not available in current workflow step";
                }

                // Enhanced: Use the new API response persistence pattern
                // Try to get engagement ID from the previous step's API response first
                var createProjectResponse = state.User.GetApiResponse("createSourcingProject");
                var engagementId = state.User.EngagementId; // Fallback to existing property

                if (createProjectResponse != null && createProjectResponse.IsSuccess)
                {
                    engagementId = state.User.GetApiResponseValue<string>("createSourcingProject", "engagementId") ?? engagementId;
                    _logger.LogInformation("📦 Retrieved engagement ID from API response history: {EngagementId}", engagementId);
                    Console.WriteLine($"📦 Retrieved engagement ID from API response history: {engagementId}");
                }
                else
                {
                    _logger.LogInformation("📦 Using engagement ID from state properties: {EngagementId}", engagementId);
                    Console.WriteLine($"📦 Using engagement ID from state properties: {engagementId}");
                }

                if (string.IsNullOrEmpty(engagementId))
                {
                    await turnContext.SendActivityAsync("❌ No engagement ID found. Please create a sourcing project first.");
                    return "Missing engagement ID";
                }

                // Get additional context from the create project response for better milestone creation
                var projectTitle = state.User.GetApiResponseValue<string>("createSourcingProject", "projectTitle") ?? "Project";
                var projectId = state.User.GetApiResponseValue<string>("createSourcingProject", "projectId") ?? state.User.ProjectId;

                _logger.LogInformation("📦 Cross-step data retrieved - ProjectTitle: {ProjectTitle}, ProjectId: {ProjectId}", 
                    projectTitle, projectId);

                // Extract milestones from user input
                var input = parameters.ContainsKey("input") ? parameters["input"]?.ToString() : turnContext.Activity.Text ?? "";
                var milestones = ExtractMilestones(input);

                if (!milestones.Any())
                {
                    await turnContext.SendActivityAsync("I couldn't find any milestones in your message. Please provide milestones in this format:\n\n" +
                        "• **Milestone 1** - due 2025-10-15\n" +
                        "• **Milestone 2** - due 2025-10-20\n\n" +
                        "Or describe your milestones with delivery dates.");
                    return "No milestones found";
                }

                await turnContext.SendActivityAsync($"🔄 Adding {milestones.Count} milestones to your sourcing project **{projectTitle}**...");

                // Call GraphQL service to upsert milestones
                var response = await _graphQLService.UpsertMilestonesAsync(engagementId, milestones);

                // Parse the response
                var responseJson = JsonDocument.Parse(response);
                if (responseJson.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("upsertEngagementInfo", out var upsertInfo) &&
                    upsertInfo.TryGetProperty("engagementMilestoneResponse", out var milestoneResponse))
                {
                    var responseEngagementId = milestoneResponse.TryGetProperty("engagementId", out var engIdElement) 
                        ? engIdElement.GetString() : "";

                    // Prepare parsed data for API response history
                    var parsedData = new Dictionary<string, object>
                    {
                        ["engagementId"] = responseEngagementId,
                        ["milestonesCount"] = milestones.Count,
                        ["projectId"] = projectId,
                        ["projectTitle"] = projectTitle
                    };

                    var responseText = $"✅ **Milestones added successfully!**\n\n" +
                        $"📋 **Project:** {projectId}\n" +
                        $"🎯 **Engagement ID:** {responseEngagementId}\n\n" +
                        $"**Confirmed Milestones:**\n";

                    // Parse and display the actual milestones returned from the API
                    var addedMilestonesCount = 0;
                    if (milestoneResponse.TryGetProperty("engagementMilestones", out var engagementMilestones) &&
                        engagementMilestones.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var milestone in engagementMilestones.EnumerateArray())
                        {
                            var title = milestone.TryGetProperty("title", out var titleElement) 
                                ? titleElement.GetString() ?? "Untitled" : "Untitled";
                            
                            var deliveryDateStr = milestone.TryGetProperty("deliveryDate", out var dateElement) 
                                ? dateElement.GetString() ?? "" : "";
                            
                            if (DateTime.TryParse(deliveryDateStr, out var deliveryDate))
                            {
                                responseText += $"• **{title}** - Due: {deliveryDate:yyyy-MM-dd}\n";
                            }
                            else
                            {
                                responseText += $"• **{title}** - Due: {deliveryDateStr}\n";
                            }
                            addedMilestonesCount++;
                        }
                    }

                    // If no milestones were returned in the response, fall back to the input milestones
                    if (addedMilestonesCount == 0)
                    {
                        foreach (var milestone in milestones)
                        {
                            responseText += $"• **{milestone.Title}** - Due: {milestone.DeliveryDate:yyyy-MM-dd}\n";
                            addedMilestonesCount++;
                        }
                    }

                    // Add milestone details to parsed data
                    parsedData["addedMilestonesCount"] = addedMilestonesCount;
                    parsedData["milestoneDetails"] = milestones.Select(m => new { m.Title, m.DeliveryDate }).ToList();

                    // Update workflow state to MILESTONES_CREATED
                    state.User.CurrentStep = WorkflowStep.MILESTONES_CREATED;
                    state.User.LastActivityTime = DateTime.UtcNow;

                    // Store API response in history for potential future steps
                    state.User.AddApiResponse("upsertMilestones", WorkflowStep.MILESTONES_CREATED, response, parsedData, true);

                    _logger.LogInformation("📦 API response stored in history. Added {Count} milestones", addedMilestonesCount);
                    _logger.LogInformation("🔄 Workflow state updated to MILESTONES_CREATED");
                    Console.WriteLine($"📦 API response stored in history. Added {addedMilestonesCount} milestones");
                    Console.WriteLine($"🔄 Workflow state updated to MILESTONES_CREATED");

                    responseText += "\n🚀 Your sourcing project is now ready with milestones defined!";
                    responseText += "\n\n🎯 **Next Step:** Ready for RFX summary generation.";

                    await turnContext.SendActivityAsync(responseText);
                    return $"Successfully added {addedMilestonesCount} milestones to engagement {engagementId}";
                }
                else
                {
                    // Store failed API response
                    state.User.AddApiResponse("upsertMilestones", WorkflowStep.MILESTONES_CREATED, response, null, false, "Failed to parse milestone upsert response");
                    
                    _logger.LogError("Failed to parse milestone upsert response: {Response}", response);
                    await turnContext.SendActivityAsync("❌ Failed to add milestones. Please try again or contact support.");
                    return "Failed to add milestones";
                }
            }
            catch (Exception ex)
            {
                // Store exception in API response history
                state.User.AddApiResponse("upsertMilestones", WorkflowStep.MILESTONES_CREATED, "", null, false, ex.Message);
                
                _logger.LogError(ex, "Error upserting milestones");
                state.User.LastError = ex.Message;
                await turnContext.SendActivityAsync("❌ An error occurred while adding milestones. Please try again.");
                return "Error upserting milestones";
            }
        }

        /// <summary>
        /// Extract milestones from user input using various patterns
        /// </summary>
        private List<ProjectMilestone> ExtractMilestones(string input)
        {
            var milestones = new List<ProjectMilestone>();

            // Pattern 1: Bullet point format with dates
            // • Milestone title - due 2025-10-15
            // - Milestone title - 2025-10-15
            var bulletPattern = @"[•\-\*]\s*(.+?)\s*[-–—]\s*(?:due\s+)?(\d{4}-\d{2}-\d{2})";
            var bulletMatches = Regex.Matches(input, bulletPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            foreach (Match match in bulletMatches)
            {
                var title = match.Groups[1].Value.Trim();
                if (DateTime.TryParse(match.Groups[2].Value, out var date))
                {
                    milestones.Add(new ProjectMilestone { Title = title, DeliveryDate = date });
                }
            }

            // Pattern 2: Numbered list format
            // 1. Milestone title - 2025-10-15
            // 1) Milestone title - due 2025-10-15
            var numberedPattern = @"\d+[.)]\s*(.+?)\s*[-–—]\s*(?:due\s+)?(\d{4}-\d{2}-\d{2})";
            var numberedMatches = Regex.Matches(input, numberedPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            foreach (Match match in numberedMatches)
            {
                var title = match.Groups[1].Value.Trim();
                if (DateTime.TryParse(match.Groups[2].Value, out var date))
                {
                    // Avoid duplicates
                    if (!milestones.Any(m => m.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                    {
                        milestones.Add(new ProjectMilestone { Title = title, DeliveryDate = date });
                    }
                }
            }

            // Pattern 3: Simple format with "by" or "due"
            // Deliver 10 xboxes by 2025-10-15
            // Complete setup due 2025-10-20
            var simplePattern = @"([^.!?\n]+?)\s+(?:by|due)\s+(\d{4}-\d{2}-\d{2})";
            var simpleMatches = Regex.Matches(input, simplePattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            foreach (Match match in simpleMatches)
            {
                var title = match.Groups[1].Value.Trim();
                if (DateTime.TryParse(match.Groups[2].Value, out var date))
                {
                    // Avoid duplicates
                    if (!milestones.Any(m => m.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                    {
                        milestones.Add(new ProjectMilestone { Title = title, DeliveryDate = date });
                    }
                }
            }

            // If no milestones found with patterns, try to extract from a more general approach
            if (!milestones.Any())
            {
                // Look for any dates and try to associate them with nearby text
                var datePattern = @"\b(\d{4}-\d{2}-\d{2})\b";
                var dateMatches = Regex.Matches(input, datePattern);

                // Split input into sentences/lines and try to match with dates
                var lines = input.Split(new[] { '\n', '\r', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (Match dateMatch in dateMatches)
                {
                    var dateStr = dateMatch.Value;
                    if (DateTime.TryParse(dateStr, out var date))
                    {
                        // Find the line containing this date
                        var relevantLine = lines.FirstOrDefault(line => line.Contains(dateStr));
                        if (!string.IsNullOrEmpty(relevantLine))
                        {
                            // Extract title by removing the date and cleaning up
                            var title = relevantLine.Replace(dateStr, "").Trim();
                            title = Regex.Replace(title, @"[-–—]\s*$", "").Trim(); // Remove trailing dashes
                            title = Regex.Replace(title, @"^\s*[•\-\*\d+[.)]]\s*", "").Trim(); // Remove leading bullets/numbers

                            if (!string.IsNullOrEmpty(title) && title.Length > 3)
                            {
                                // Avoid duplicates
                                if (!milestones.Any(m => m.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                                {
                                    milestones.Add(new ProjectMilestone { Title = title, DeliveryDate = date });
                                }
                            }
                        }
                    }
                }
            }

            return milestones;
        }
    }
}