using Microsoft.Bot.Builder;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Action;
using MyM365Agent1.Model;

namespace MyM365Agent1.Actions
{
    /// <summary>
    /// Action to handle cancellation of the project form
    /// </summary>
    public class CancelProjectFormAction
    {
        private readonly ILogger<CancelProjectFormAction> _logger;

        public CancelProjectFormAction(ILogger<CancelProjectFormAction> logger)
        {
            _logger = logger;
        }

        [Action("cancelProjectForm")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext, 
            [ActionTurnState] AppState state, 
            [ActionParameters] Dictionary<string, object> parameters)
        {
            // FORM ACTION DISABLED - This action has been disabled in favor of text-based project creation
            await turnContext.SendActivityAsync("❌ Form cancellation is disabled. Please use text-based project creation instead. Say 'create project' to get started.");
            return "Form action disabled - redirected to text-based flow";
        }

        /* ORIGINAL IMPLEMENTATION COMMENTED OUT
        public async Task<string> ExecuteAsyncOriginal(
            [ActionTurnContext] ITurnContext turnContext, 
            [ActionTurnState] AppState state, 
            [ActionParameters] Dictionary<string, object> parameters)
        {
            try
            {
                _logger.LogInformation("User cancelled project form");

                await turnContext.SendActivityAsync("❌ **Project creation cancelled.**\n\n" +
                    "You can start again anytime by asking me to create a new sourcing project.");

                // Reset state if needed
                state.User.CurrentStep = WorkflowStep.PROJECT_TO_BE_CREATED;

                return "Project creation cancelled by user";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling project form cancellation");
                await turnContext.SendActivityAsync("Project creation has been cancelled.");
                return "Error handling cancellation";
            }
        }
        */
    }
}