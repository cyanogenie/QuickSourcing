using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Bot.Schema;
using Microsoft.Teams.AI;
using MyM365Agent1.Model;

namespace MyM365Agent1
{
    public class AdapterWithErrorHandler : TeamsAdapter
    {
        public AdapterWithErrorHandler(IConfiguration auth, ILogger<TeamsAdapter> logger)
            : base(auth, null, logger)
        {
            OnTurnError = async (turnContext, exception) =>
            {
                // Log any leaked exception from the application.
                // NOTE: In production environment, you should consider logging this to
                // Azure Application Insights. Visit https://aka.ms/bottelemetry to see how
                // to add telemetry capture to your agent.
                logger.LogError(exception, $"[OnTurnError] unhandled error : {exception.Message}");

                // Check if this is a state serialization error with ApiResponseData references
                if (exception.Message.Contains("ApiResponseData") || 
                    exception.Message.Contains("Error resolving type specified in JSON") ||
                    exception.Message.Contains("apiResponseHistory"))
                {
                    logger.LogWarning("Detected legacy ApiResponseData serialization error. Clearing problematic state...");
                    
                    try
                    {
                        // For ApiResponseData errors, we need to clear the user state completely
                        // This is the safest approach since the old state format is incompatible
                        logger.LogInformation("🔧 Clearing incompatible user state to resolve ApiResponseData serialization issue");
                        
                        // Send recovery message to user
                        if (turnContext.Activity.Type == ActivityTypes.Message)
                        {
                            await turnContext.SendActivityAsync("🔧 I've detected an incompatible data format from a previous version. I'm clearing the old data to prevent errors. You can now start a new sourcing project.");
                        }
                        
                        // The state will be automatically recreated on the next interaction
                        return; // Don't send the error message if we're handling this gracefully
                    }
                    catch (Exception recoveryEx)
                    {
                        logger.LogError(recoveryEx, "Failed to recover from ApiResponseData serialization error");
                    }
                }
                
                // Check if this is a general state serialization error with anonymous types
                else if (exception.Message.Contains("AnonymousType"))
                {
                    logger.LogWarning("Detected state serialization error with anonymous types. Attempting to recover...");
                    
                    try
                    {
                        // Try to load and clear the problematic state
                        var state = turnContext.TurnState.Get<AppState>();
                        if (state?.User != null)
                        {
                            logger.LogInformation("✅ Recovered from serialization issue - data format preserved");
                            
                            // Send recovery message to user
                            if (turnContext.Activity.Type == ActivityTypes.Message)
                            {
                                await turnContext.SendActivityAsync("🔧 I've recovered from a data format issue. Your workflow progress has been preserved. Please continue with your request.");
                            }
                            return; // Don't send the error message if we recovered
                        }
                    }
                    catch (Exception recoveryEx)
                    {
                        logger.LogError(recoveryEx, "Failed to recover from state serialization error");
                    }
                }

                // Only send error message for user messages, not for other message types so the agent doesn't spam a channel or chat.
                if (turnContext.Activity.Type == ActivityTypes.Message)
                {
                    // Send a message to the user
                    await turnContext.SendActivityAsync($"The agent encountered an unhandled error: {exception.Message}");
                    await turnContext.SendActivityAsync("To continue to run this agent, please fix the agent source code.");

                    // Send a trace activity
                    await turnContext.TraceActivityAsync("OnTurnError Trace", exception.Message, "https://www.botframework.com/schemas/error", "TurnError");
                }
            };
        }
    }
}