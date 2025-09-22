using Microsoft.Bot.Builder;
using Microsoft.Teams.AI.AI.Action;
using Microsoft.Teams.AI;
using MyM365Agent1.Model;
using MyM365Agent1.Services;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MyM365Agent1.Actions
{
    /// <summary>
    /// Action to publish a sourcing project after showing project details for confirmation
    /// </summary>
    public class PublishProjectAction
    {
        private readonly IGraphQLService _graphQLService;
        private readonly ILogger<PublishProjectAction> _logger;

        public PublishProjectAction(IGraphQLService graphQLService, ILogger<PublishProjectAction> logger)
        {
            _graphQLService = graphQLService;
            _logger = logger;
        }

        [Action("publishProject")]
        [Description("Publishes the sourcing project after showing project details and getting user confirmation")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext,
            [ActionTurnState] AppState state)
        {
            try
            {
                _logger.LogInformation("🚀 PublishProjectAction triggered");
                Console.WriteLine("🚀 PublishProjectAction: Starting project publication process");

                // Validate current state
                if (state.User.CurrentStep != WorkflowStep.SUPPLIERS_SELECTED)
                {
                    var currentStep = state.User.CurrentStep;
                    _logger.LogWarning("❌ PublishProject called in invalid state: {CurrentStep}", currentStep);
                    return $"❌ You can only publish a project after selecting suppliers. Current step: {currentStep}. Please complete the supplier selection first.";
                }

                // Get project data
                var projectId = state.User.ProjectId;
                var engagementId = state.User.EngagementId;

                if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(engagementId))
                {
                    _logger.LogError("❌ Missing project data - ProjectId: {ProjectId}, EngagementId: {EngagementId}", projectId, engagementId);
                    return "❌ Project information is missing. Please create a project first.";
                }

                // Get project details from stored simple properties
                var projectTitle = state.User.ProjectTitle;
                var projectDescription = state.User.ProjectDescription;
                var milestonesJson = state.User.MilestonesJson;

                if (string.IsNullOrEmpty(projectTitle))
                {
                    _logger.LogError("❌ No project data found in state");
                    return "❌ Project details not found. Please create a project first.";
                }

                // Get dates from project creation or use defaults
                var supplierResponseStartDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var supplierResponseDueBy = DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var awardTargetDate = DateTime.UtcNow.AddDays(16).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                // Try to get dates from milestones if available
                if (!string.IsNullOrEmpty(milestonesJson))
                {
                    var milestones = GetMilestonesFromJson(milestonesJson);
                    if (milestones.Any())
                    {
                        var lastMilestone = milestones.OrderBy(m => m.DeliveryDate).Last();
                        awardTargetDate = lastMilestone.DeliveryDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    }
                }

                // Show project details for confirmation
                var projectDetailsMessage = BuildProjectDetailsMessage(
                    projectId, 
                    projectTitle, 
                    supplierResponseStartDate, 
                    supplierResponseDueBy, 
                    awardTargetDate
                );

                await turnContext.SendActivityAsync(projectDetailsMessage);

                // Ask for confirmation
                await turnContext.SendActivityAsync("🔔 **Ready to publish?** Type 'yes' or 'confirm' to publish this project to suppliers, or 'cancel' to abort.");

                return "📋 Project details displayed. Please confirm if you want to publish this project.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in PublishProjectAction");
                Console.WriteLine($"❌ PublishProjectAction error: {ex.Message}");
                return $"❌ Error preparing project for publication: {ex.Message}";
            }
        }

        [Action("confirmPublishProject")]
        [Description("Confirms and executes the project publication to suppliers")]
        public async Task<string> ConfirmPublishAsync(
            [ActionTurnContext] ITurnContext turnContext,
            [ActionTurnState] AppState state)
        {
            try
            {
                _logger.LogInformation("✅ ConfirmPublishProject triggered");
                Console.WriteLine("✅ ConfirmPublishProject: Publishing project");

                // Validate current state
                if (state.User.CurrentStep != WorkflowStep.SUPPLIERS_SELECTED)
                {
                    return "❌ Invalid state for project publication.";
                }

                var projectId = state.User.ProjectId;
                var engagementId = state.User.EngagementId;

                // Get project details from simple properties
                var projectTitle = state.User.ProjectTitle ?? "Project";

                // Calculate dates
                var supplierResponseStartDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var supplierResponseDueBy = DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var awardTargetDate = DateTime.UtcNow.AddDays(16).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                // Try to get award target date from milestones
                var milestonesJson = state.User.MilestonesJson;
                if (!string.IsNullOrEmpty(milestonesJson))
                {
                    var milestones = GetMilestonesFromJson(milestonesJson);
                    if (milestones.Any())
                    {
                        var lastMilestone = milestones.OrderBy(m => m.DeliveryDate).Last();
                        awardTargetDate = lastMilestone.DeliveryDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    }
                }

                await turnContext.SendActivityAsync("🚀 Publishing your project to suppliers...");

                // Call GraphQL API to publish project
                var response = await _graphQLService.PublishProjectAsync(
                    int.Parse(projectId),
                    projectTitle,
                    supplierResponseStartDate,
                    supplierResponseDueBy,
                    awardTargetDate
                );

                _logger.LogInformation("📤 PublishProject API response: {Response}", response);

                // Parse response to check for success
                var isSuccess = IsPublishSuccessful(response);

                if (isSuccess)
                {
                    // Store successful publication response in simple properties
                    state.User.LastApiResponse = response;
                    
                    // Update workflow state to PUBLISHED
                    state.User.CurrentStep = WorkflowStep.PUBLISHED;
                    _logger.LogInformation("🔄 State updated: CurrentStep=PUBLISHED, ProjectId={ProjectId}", projectId);

                    var successMessage = $@"🎉 **Project Published Successfully!**

✅ **Project ID:** {projectId}
📋 **Title:** {projectTitle}
📅 **Supplier Response Due:** {DateTime.Parse(supplierResponseDueBy):MMM dd, yyyy}
🎯 **Award Target Date:** {DateTime.Parse(awardTargetDate):MMM dd, yyyy}

🌐 **Suppliers can now respond via the Supplier Web Portal!** They will be able to view the project details and submit their proposals through the portal.

🔔 **Next Steps:**
- Monitor supplier responses in the portal
- Evaluate proposals when they come in
- Proceed with vendor selection and award process

Thank you for using the sourcing system! 🚀

🔄 **Ready for a new project?** The workflow has been reset and you can now start creating a new sourcing project.";

                    // Reset the workflow for a new project
                    ResetWorkflow(state);
                    _logger.LogInformation("🔄 Workflow reset complete - ready for new project");

                    return successMessage;
                }
                else
                {
                    // Publication failed
                    state.User.LastApiResponse = response;
                    
                    _logger.LogError("❌ Project publication failed: {Response}", response);
                    return $"❌ Failed to publish project. API Response: {response}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error confirming project publication");
                return $"❌ Error publishing project: {ex.Message}";
            }
        }

        /// <summary>
        /// Resets the workflow state to start a new sourcing project
        /// </summary>
        private void ResetWorkflow(AppState state)
        {
            try
            {
                _logger.LogInformation("🔄 Starting workflow reset");
                
                // Reset the workflow step to the beginning
                state.User.CurrentStep = WorkflowStep.PROJECT_TO_BE_CREATED;
                
                // Clear all project-related data but keep user identity
                var emailId = state.User.EmailId; // Preserve user identity
                
                // Reset project data
                state.User.ProjectId = string.Empty;
                state.User.EngagementId = string.Empty;
                state.User.ProjectTitle = string.Empty;
                state.User.ProjectDescription = string.Empty;
                state.User.MilestonesJson = string.Empty;
                state.User.SuppliersJson = string.Empty;
                state.User.LastApiResponse = string.Empty;
                state.User.LastError = string.Empty;
                
                // Update activity time
                state.User.LastActivityTime = DateTime.UtcNow;
                
                // Generate new state ID for the new project
                state.User.StateId = DateTime.UtcNow.ToString("yyyyMMddHHmm");
                
                _logger.LogInformation("🔄 Workflow reset complete - CurrentStep={CurrentStep}, StateId={StateId}", 
                    state.User.CurrentStep, state.User.StateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error resetting workflow");
            }
        }

        private string BuildProjectDetailsMessage(
            string projectId, 
            string projectTitle, 
            string supplierResponseStartDate,
            string supplierResponseDueBy, 
            string awardTargetDate)
        {
            var startDate = DateTime.Parse(supplierResponseStartDate);
            var dueDate = DateTime.Parse(supplierResponseDueBy);
            var targetDate = DateTime.Parse(awardTargetDate);

            var message = $@"� **Project Publication Details**

┌─────────────────────────────────────────────────────────────┐
│                    📊 **PROJECT INFORMATION**               │
└─────────────────────────────────────────────────────────────┘

📋 **Sourcing Project ID:** `{projectId}`  
🏷️ **Title:** {projectTitle}  
🏢 **Company Code:** `1010`  
📝 **Type:** Request for quote (RFQ)  
💰 **Currency:** USD  
📈 **Status:** Draft → **Ready for Publication** ✅  

┌─────────────────────────────────────────────────────────────┐
│                     📅 **SCHEDULE TIMELINE**                │
└─────────────────────────────────────────────────────────────┘

🚀 **Release Date to Supplier:** `{startDate:MMM dd, yyyy}`  
⏰ **Supplier Response Due By:** `{dueDate:MMM dd, yyyy}` ⭐  
🎯 **Award Target Date:** `{targetDate:MMM dd, yyyy}` ⭐  

┌─────────────────────────────────────────────────────────────┐
│                   👥 **SUPPLIER & SETTINGS**                │
└─────────────────────────────────────────────────────────────┘

✅ **Selected Suppliers:** Available suppliers ready to receive RFQ  
📧 **Reminder Mails:** Enabled ✅  
👤 **Microsoft Contacts:** To be configured  

─────────────────────────────────────────────────────────────

🔔 **Ready to Publish!** This project will be sent to selected suppliers who can then view the requirements and submit their proposals.";

            return message;
        }

        private List<ProjectMilestone> GetMilestonesFromJson(string milestonesJson)
        {
            var milestones = new List<ProjectMilestone>();
            
            if (string.IsNullOrEmpty(milestonesJson))
                return milestones;

            try
            {
                using var document = JsonDocument.Parse(milestonesJson);
                var jsonRoot = document.RootElement;
                
                if (jsonRoot.ValueKind == JsonValueKind.Array)
                {
                    foreach (var milestone in jsonRoot.EnumerateArray())
                    {
                        var title = milestone.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "";
                        var dateStr = milestone.TryGetProperty("deliveryDate", out var dateProp) ? dateProp.GetString() : "";
                        
                        if (!string.IsNullOrEmpty(title) && DateTime.TryParse(dateStr, out var deliveryDate))
                        {
                            milestones.Add(new ProjectMilestone { Title = title, DeliveryDate = deliveryDate });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse milestones from JSON: {Json}", milestonesJson);
            }
            
            return milestones;
        }

        private bool IsPublishSuccessful(string response)
        {
            try
            {
                if (string.IsNullOrEmpty(response))
                    return false;

                // Parse JSON response to check for success indicators
                using var doc = JsonDocument.Parse(response);
                
                // Check for errors first
                if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                {
                    return false;
                }

                // Check for data.publishProject
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("publishProject", out var publishProject))
                {
                    // If we have a projectId in the response, consider it successful
                    return publishProject.TryGetProperty("projectId", out var _);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error parsing publish response: {Error}", ex.Message);
                return false;
            }
        }

        public class ProjectMilestone
        {
            public string Title { get; set; } = "";
            public DateTime DeliveryDate { get; set; }
        }
    }
}