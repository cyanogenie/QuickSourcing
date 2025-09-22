using Microsoft.Bot.Builder;
using Microsoft.Teams.AI.AI.Action;
using Microsoft.Teams.AI;
using MyM365Agent1.Model;
using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace MyM365Agent1.Actions
{
    /// <summary>
    /// Action to handle confirmation responses for publishing projects
    /// </summary>
    public class ConfirmPublishAction
    {
        private readonly PublishProjectAction _publishProjectAction;
        private readonly ILogger<ConfirmPublishAction> _logger;

        public ConfirmPublishAction(PublishProjectAction publishProjectAction, ILogger<ConfirmPublishAction> logger)
        {
            _publishProjectAction = publishProjectAction;
            _logger = logger;
        }

        [Action("confirmPublish")]
        [Description("Confirms and executes project publication when user confirms")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext,
            [ActionTurnState] AppState state)
        {
            try
            {
                _logger.LogInformation("✅ ConfirmPublish triggered - executing publication");
                
                // Call the confirmation method from PublishProjectAction
                return await _publishProjectAction.ConfirmPublishAsync(turnContext, state);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in ConfirmPublishAction");
                return $"❌ Error confirming publication: {ex.Message}";
            }
        }
    }
}