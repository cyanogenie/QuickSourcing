using Microsoft.Bot.Builder;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Action;
using MyM365Agent1.Model;

namespace MyM365Agent1.Actions
{
    /// <summary>
    /// Action to reset the workflow state and start over
    /// </summary>
    public class ResetWorkflowAction
    {
        private readonly ILogger<ResetWorkflowAction> _logger;

        public ResetWorkflowAction(ILogger<ResetWorkflowAction> logger)
        {
            _logger = logger;
        }

        [Action("resetWorkflow")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext, 
            [ActionTurnState] AppState state, 
            [ActionParameters] Dictionary<string, object> parameters)
        {
            try
            {
                _logger.LogInformation("Resetting workflow state for user");

                // Clear all user state and start fresh
                state.User.CurrentStep = WorkflowStep.PROJECT_TO_BE_CREATED;
                state.User.EmailId = string.Empty;
                state.User.ProjectId = string.Empty;
                state.User.EngagementId = string.Empty;
                state.User.LastError = string.Empty;
                state.User.LastActivityTime = DateTime.UtcNow;
                // Generate new state ID for the session
                state.User.StateId = DateTime.UtcNow.ToString("yyyyMMddHHmm");

                await turnContext.SendActivityAsync("🔄 **Workflow Reset Complete!**\n\n" +
                    "I've reset our conversation and I'm ready to help you create a new sourcing project.\n\n" +
                    "To get started, please provide:\n" +
                    "• **Project Title**\n" +
                    "• **Project Description** (what you want to source)\n" +
                    "• **Your Email Address**\n" +
                    "• **Start Date** (optional, defaults to tomorrow)\n" +
                    "• **End Date** (optional, defaults to 30 days from now)\n" +
                    "• **Approximate Budget** in USD (optional, defaults to $1,000)\n\n" +
                    "You can provide all this information in one message or I'll ask for missing details.");

                return "Workflow reset successfully, ready for new sourcing project";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting workflow");
                await turnContext.SendActivityAsync("❌ There was an error resetting the workflow. Please try again.");
                return "Error resetting workflow";
            }
        }
    }
}