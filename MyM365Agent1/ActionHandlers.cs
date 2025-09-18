using Microsoft.Bot.Builder;
using Microsoft.Teams.AI.AI;
using Microsoft.Teams.AI.AI.Action;
using MyM365Agent1.Model;
using MyM365Agent1.Services;
using System.Text.Json;

namespace MyM365Agent1
{
    public class ActionHandlers
    {
        private readonly IExternalApiService _externalApiService;
        private readonly IGraphQLService _graphQLService;
        private readonly ILogger<ActionHandlers> _logger;

        public ActionHandlers(IExternalApiService externalApiService = null, IGraphQLService graphQLService = null, ILogger<ActionHandlers> logger = null)
        {
            _externalApiService = externalApiService;
            _graphQLService = graphQLService;
            _logger = logger;
        }

        [Action(AIConstants.HttpErrorActionName)]
        public async Task<string> OnHttpError([ActionTurnContext] ITurnContext turnContext)
        {
            await turnContext.SendActivityAsync("An AI request failed. Please try again later.");
            return AIConstants.StopCommand;
        }

        [Action("createTask")]
        public Task<string> OnCreateTask([ActionTurnContext] ITurnContext turnContext, [ActionTurnState] AppState state, [ActionParameters] Dictionary<string, object> entities)
        {
            string title = entities["title"].ToString();
            string description = entities["description"].ToString();
            MyTask task = new MyTask
            {
                Title = title,
                Description = description
            };
            Dictionary<string, MyTask> tasks = state.Conversation.Tasks;
            tasks[title] = task;
            state.Conversation.Tasks = tasks;
            return Task.FromResult("task created, think about your next action");
        }

        [Action("deleteTask")]
        public async Task<string> OnDeleteTask([ActionTurnContext] ITurnContext turnContext, [ActionTurnState] AppState state, [ActionParameters] Dictionary<string, object> entities)
        {
            string title = entities["title"].ToString();
            Dictionary<string, MyTask> tasks = state.Conversation.Tasks;
            if (tasks.ContainsKey(title))
            {
                tasks.Remove(title);
                state.Conversation.Tasks = tasks;
                return "task has been deleted. Think about your next action";
            }
            else
            {
                await turnContext.SendActivityAsync($"There is no task '{title}'.");
                return "task not found, think about your next action";
            }
        }

        [Action("postToExternalApi")]
        public async Task<string> OnPostToExternalApi([ActionTurnContext] ITurnContext turnContext, [ActionTurnState] AppState state, [ActionParameters] Dictionary<string, object> entities)
        {
            if (_externalApiService == null)
            {
                await turnContext.SendActivityAsync("External API service is not configured.");
                return "External API service unavailable, think about your next action";
            }

            try
            {
                // Extract parameters from entities
                string endpoint = entities.ContainsKey("endpoint") ? entities["endpoint"].ToString() : "";
                object data = entities.ContainsKey("data") ? entities["data"] : new { };

                if (string.IsNullOrEmpty(endpoint))
                {
                    await turnContext.SendActivityAsync("Please provide an endpoint for the API call.");
                    return "Missing endpoint parameter, think about your next action";
                }

                // Optional: Extract custom headers if provided
                Dictionary<string, string> headers = null;
                if (entities.ContainsKey("headers") && entities["headers"] != null)
                {
                    try
                    {
                        headers = JsonSerializer.Deserialize<Dictionary<string, string>>(entities["headers"].ToString());
                    }
                    catch
                    {
                        // If headers parsing fails, continue without them
                        headers = null;
                    }
                }

                // Make the API call
                string response = await _externalApiService.PostAsync(endpoint, data, headers);

                // Send response back to user
                await turnContext.SendActivityAsync($"API call successful. Response: {response}");
                
                return "External API call completed successfully, think about your next action";
            }
            catch (Exception ex)
            {
                await turnContext.SendActivityAsync($"Error calling external API: {ex.Message}");
                return "External API call failed, think about your next action";
            }
        }

        /*
        [Action("getEventDetails")]
        public async Task<string> OnGetEventDetails([ActionTurnContext] ITurnContext turnContext, [ActionTurnState] AppState state, [ActionParameters] Dictionary<string, object> entities)
        {
            if (_graphQLService == null)
            {
                await turnContext.SendActivityAsync("GraphQL service is not configured.");
                return "GraphQL service unavailable, think about your next action";
            }

            try
            {
                // Extract event ID from entities
                if (!entities.ContainsKey("eventId"))
                {
                    await turnContext.SendActivityAsync("Please provide an event ID to get event details.");
                    return "Missing eventId parameter, think about your next action";
                }

                // Parse event ID
                if (!int.TryParse(entities["eventId"].ToString(), out int eventId))
                {
                    await turnContext.SendActivityAsync("Please provide a valid numeric event ID.");
                    return "Invalid eventId parameter, think about your next action";
                }

                _logger?.LogInformation($"Getting event details for event ID: {eventId}");

                // Make the GraphQL call
                string response = await _graphQLService.GetEventDetailsAsync(eventId);

                // Parse and format the response for better readability
                var jsonDocument = JsonDocument.Parse(response);
                var formattedResponse = JsonSerializer.Serialize(jsonDocument, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // Send response back to user
                await turnContext.SendActivityAsync($"Event details retrieved successfully for Event ID {eventId}:\n```json\n{formattedResponse}\n```");
                
                return "Event details retrieved successfully, think about your next action";
            }
            catch (Exception ex)
            {
                await turnContext.SendActivityAsync($"Error retrieving event details: {ex.Message}");
                return "Failed to retrieve event details, think about your next action";
            }
        }
        */

        [Action("executeGraphQLQuery")]
        public async Task<string> OnExecuteGraphQLQuery([ActionTurnContext] ITurnContext turnContext, [ActionTurnState] AppState state, [ActionParameters] Dictionary<string, object> entities)
        {
            if (_graphQLService == null)
            {
                await turnContext.SendActivityAsync("GraphQL service is not configured.");
                return "GraphQL service unavailable, think about your next action";
            }

            try
            {
                // Extract query from entities
                if (!entities.ContainsKey("query"))
                {
                    await turnContext.SendActivityAsync("Please provide a GraphQL query to execute.");
                    return "Missing query parameter, think about your next action";
                }

                string query = entities["query"].ToString();

                // Extract variables if provided
                object variables = null;
                if (entities.ContainsKey("variables") && entities["variables"] != null)
                {
                    try
                    {
                        variables = JsonSerializer.Deserialize<object>(entities["variables"].ToString());
                    }
                    catch
                    {
                        // If variables parsing fails, continue without them
                        variables = null;
                    }
                }

                // Make the GraphQL call
                string response = await _graphQLService.ExecuteQueryAsync(query, variables);

                // Parse and format the response for better readability
                var jsonDocument = JsonDocument.Parse(response);
                var formattedResponse = JsonSerializer.Serialize(jsonDocument, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // Send response back to user
                await turnContext.SendActivityAsync($"GraphQL query executed successfully:\n```json\n{formattedResponse}\n```");
                
                return "GraphQL query executed successfully, think about your next action";
            }
            catch (Exception ex)
            {
                await turnContext.SendActivityAsync($"Error executing GraphQL query: {ex.Message}");
                return "GraphQL query execution failed, think about your next action";
            }
        }
    }
}
