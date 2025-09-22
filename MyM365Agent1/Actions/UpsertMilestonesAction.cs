using Microsoft.Bot.Builder;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Action;
using MyM365Agent1.Model;
using MyM365Agent1.Services;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

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

                // Get engagement ID and project details from simple state properties
                var engagementId = state.User.EngagementId;
                var projectTitle = state.User.ProjectTitle ?? "Project";
                var projectId = state.User.ProjectId;

                _logger.LogInformation("📦 Retrieved data from state - ProjectTitle: {ProjectTitle}, ProjectId: {ProjectId}, EngagementId: {EngagementId}", 
                    projectTitle, projectId, engagementId);

                if (string.IsNullOrEmpty(engagementId))
                {
                    await turnContext.SendActivityAsync("❌ No engagement ID found. Please create a sourcing project first.");
                    return "Missing engagement ID";
                }

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

                // Use proper grammar for singular/plural
                var milestoneText = milestones.Count == 1 ? "milestone" : "milestones";
                await turnContext.SendActivityAsync($"🔄 Adding {milestones.Count} {milestoneText} to your sourcing project **{projectTitle}**...");

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

                    var responseText = $"🎯 **Milestones Added Successfully!**\n\n" +
                        $"┌─────────────────────────────────────────────────────────────┐\n" +
                        $"│                📋 **MILESTONE OVERVIEW**                    │\n" +
                        $"└─────────────────────────────────────────────────────────────┘\n\n" +
                        $"🆔 **Project ID:** `{projectId}`\n" +
                        $"🔗 **Engagement ID:** `{responseEngagementId}`\n\n" +
                        $"📅 **Confirmed Project Milestones:**\n\n" +
                        $"| **Milestone Description** | **Delivery Date** |\n" +
                        $"|---------------------------|-------------------|\n";

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
                                responseText += $"| {title} | {deliveryDate:yyyy-MM-dd} |\n";
                            }
                            else
                            {
                                responseText += $"| {title} | {deliveryDateStr} |\n";
                            }
                            addedMilestonesCount++;
                        }
                    }

                    // If no milestones were returned in the response, fall back to the input milestones
                    if (addedMilestonesCount == 0)
                    {
                        foreach (var milestone in milestones)
                        {
                            responseText += $"| {milestone.Title} | {milestone.DeliveryDate:yyyy-MM-dd} |\n";
                            addedMilestonesCount++;
                        }
                    }

                    // Add table summary
                    responseText += $"\n📊 **Total: {addedMilestonesCount} milestone{(addedMilestonesCount == 1 ? "" : "s")} confirmed**\n";

                    // Add milestone details to parsed data
                    parsedData["addedMilestonesCount"] = addedMilestonesCount;
                    parsedData["milestoneDetails"] = milestones.Select(m => new MilestoneData { Title = m.Title, DeliveryDate = m.DeliveryDate }).ToList();

                    // Update workflow state to MILESTONES_CREATED
                    state.User.CurrentStep = WorkflowStep.MILESTONES_CREATED;
                    state.User.LastActivityTime = DateTime.UtcNow;

                    // Store milestones data as simple JSON string to avoid serialization issues
                    if (parsedData.ContainsKey("milestones"))
                    {
                        state.User.MilestonesJson = parsedData["milestones"].ToString();
                    }

                    // API response history storage removed to prevent serialization issues

                    _logger.LogInformation("📦 Milestones data stored as simple JSON. Added {Count} milestones", addedMilestonesCount);
                    _logger.LogInformation("🔄 Workflow state updated to MILESTONES_CREATED");
                    Console.WriteLine($"📦 Milestones data stored as simple JSON. Added {addedMilestonesCount} milestones");
                    Console.WriteLine($"🔄 Workflow state updated to MILESTONES_CREATED");

                    responseText += "\n─────────────────────────────────────────────────────────────\n";
                    responseText += "\n🚀 **Project Setup Complete!** Your sourcing project is now ready with milestones defined!";
                    responseText += "\n\n🔍 **Next Step:** Finding relevant suppliers for your project...";

                    await turnContext.SendActivityAsync(responseText);
                    
                    // Let the AI system handle the automatic progression to findSuppliers
                    // The updated prompt will ensure findSuppliers is called automatically
                    
                    return $"Successfully added {addedMilestonesCount} milestones to engagement {engagementId}. Ready to find suppliers.";
                }
                else
                {
                    // API response history storage removed to prevent serialization issues
                    
                    _logger.LogError("Failed to parse milestone upsert response: {Response}", response);
                    await turnContext.SendActivityAsync("❌ Failed to add milestones. Please try again or contact support.");
                    return "Failed to add milestones";
                }
            }
            catch (Exception ex)
            {
                // API response history storage removed to prevent serialization issues
                
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

            // Pattern 4: Colon-separated format with "Date :" prefix
            // 1: milestone title, Date : 2025-10-15; 2: another milestone, Date : 2025-10-20
            var colonPattern = @"(\d+):\s*([^,]+),\s*Date\s*:\s*(\d{4}-\d{2}-\d{2})";
            var colonMatches = Regex.Matches(input, colonPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            foreach (Match match in colonMatches)
            {
                var title = match.Groups[2].Value.Trim();
                if (DateTime.TryParse(match.Groups[3].Value, out var date))
                {
                    // Avoid duplicates
                    if (!milestones.Any(m => m.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                    {
                        milestones.Add(new ProjectMilestone { Title = title, DeliveryDate = date });
                    }
                }
            }

            // Pattern 4: Colon-separated format with "Date :" prefix
            // 1: milestone title, Date : 2025-10-15; 2: another milestone, Date : 2025-10-20
            var colonDatePattern = @"(\d+):\s*([^,]+),\s*Date\s*:\s*(\d{4}-\d{2}-\d{2})";
            var colonDateMatches = Regex.Matches(input, colonDatePattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            foreach (Match match in colonDateMatches)
            {
                var title = match.Groups[2].Value.Trim();
                if (DateTime.TryParse(match.Groups[3].Value, out var date))
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
                var lines = input.Split(new[] { '\n', '\r', '.', '!', '?', ';' }, StringSplitOptions.RemoveEmptyEntries);
                
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
                            title = Regex.Replace(title, @"^\s*[•\-\*\d+[.):]\s*", "").Trim(); // Remove leading bullets/numbers
                            title = Regex.Replace(title, @",\s*Date\s*:\s*$", "", RegexOptions.IgnoreCase).Trim(); // Remove ", Date :"

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

    /// <summary>
    /// Simple data class for milestone information to avoid serialization issues with anonymous types
    /// </summary>
    public class MilestoneData
    {
        public string Title { get; set; } = "";
        public DateTime DeliveryDate { get; set; }
    }
}