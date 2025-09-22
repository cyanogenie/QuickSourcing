using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Action;
using MyM365Agent1.Model;
using System.Text.Json;

namespace MyM365Agent1.Actions
{
    /// <summary>
    /// Action to show an adaptive card form for collecting project details
    /// </summary>
    public class ShowProjectFormAction
    {
        private readonly ILogger<ShowProjectFormAction> _logger;

        public ShowProjectFormAction(ILogger<ShowProjectFormAction> logger)
        {
            _logger = logger;
        }

        [Action("showProjectForm")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext, 
            [ActionTurnState] AppState state, 
            [ActionParameters] Dictionary<string, object> parameters)
        {
            // FORM ACTION DISABLED - This action has been disabled in favor of text-based project creation
            await turnContext.SendActivityAsync("❌ Form-based project creation is disabled. Please use text-based project creation instead. Say 'create project' to get started.");
            return "Form action disabled - redirected to text-based flow";
        }
            
            /* ORIGINAL IMPLEMENTATION COMMENTED OUT
            try
            {
                _logger.LogInformation("🚫 ShowProjectFormAction CALLED but DISABLED - Redirecting to text-based flow");
                Console.WriteLine("🚫 ShowProjectFormAction CALLED but DISABLED - Redirecting to text-based flow");

                // Instead of showing the card, provide text-based instructions
                var promptText = @"🚀 **Let's create a new sourcing project!**

Please provide the project details in this format:

**Project Title:** [Your project title]
**Description:** [Brief description of the project]
**Email:** [Your email address]
**Start Date:** [YYYY-MM-DD format, optional]
**End Date:** [YYYY-MM-DD format, optional]
**Budget:** [Budget amount, optional]

You can also provide the details in a more natural way, and I'll extract the information for you.

**Example:**
""Create a project called 'New Website Design' with description 'Redesign company website for better user experience', email john@company.com, start date 2025-01-01, end date 2025-06-30, budget 50000""";

                await turnContext.SendActivityAsync(MessageFactory.Text(promptText));

                _logger.LogInformation("✅ Text-based project instructions sent instead of adaptive card");
                Console.WriteLine("✅ Text-based project instructions sent instead of adaptive card");
                return "Text-based project instructions provided successfully";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error displaying project form");
                Console.WriteLine($"❌ Error in ShowProjectFormAction: {ex.Message}");
                await turnContext.SendActivityAsync("❌ An error occurred while displaying the project form. Please try again.");
                return "Error displaying project form";
            }
        }

        /// <summary>
        /// Creates the adaptive card for collecting project details
        /// </summary>
        private object CreateProjectFormCard()
        {
            // Get current date for default values
            var today = DateTime.UtcNow;
            var defaultStartDate = today.AddDays(7).ToString("yyyy-MM-dd");
            var defaultEndDate = today.AddDays(14).ToString("yyyy-MM-dd");

            return new
            {
                type = "AdaptiveCard",
                version = "1.5",
                body = new object[]
                {
                    new
                    {
                        type = "TextBlock",
                        text = "🎯 Create Sourcing Project",
                        weight = "Bolder",
                        size = "Large",
                        color = "Accent"
                    },
                    new
                    {
                        type = "TextBlock",
                        text = "Please provide the following details for your sourcing project:",
                        wrap = true,
                        spacing = "Medium"
                    },
                    new
                    {
                        type = "Input.Text",
                        id = "projectTitle",
                        label = "Project Title *",
                        placeholder = "Enter a descriptive title for your project",
                        isRequired = true,
                        errorMessage = "Project title is required"
                    },
                    new
                    {
                        type = "Input.Text",
                        id = "description",
                        label = "Project Description *",
                        placeholder = "Describe what you want to source and any specific requirements",
                        isMultiline = true,
                        isRequired = true,
                        errorMessage = "Project description is required"
                    },
                    new
                    {
                        type = "Input.Text",
                        id = "email",
                        label = "Email Address *",
                        placeholder = "your.email@company.com",
                        isRequired = true,
                        errorMessage = "Valid email address is required",
                        style = "Email"
                    },
                    new
                    {
                        type = "ColumnSet",
                        columns = new object[]
                        {
                            new
                            {
                                type = "Column",
                                width = "stretch",
                                items = new object[]
                                {
                                    new
                                    {
                                        type = "Input.Date",
                                        id = "startDate",
                                        label = "Start Date *",
                                        value = defaultStartDate,
                                        isRequired = true,
                                        errorMessage = "Start date is required"
                                    }
                                }
                            },
                            new
                            {
                                type = "Column",
                                width = "stretch",
                                items = new object[]
                                {
                                    new
                                    {
                                        type = "Input.Date",
                                        id = "endDate",
                                        label = "End Date *",
                                        value = defaultEndDate,
                                        isRequired = true,
                                        errorMessage = "End date is required"
                                    }
                                }
                            }
                        }
                    },
                    new
                    {
                        type = "Input.Number",
                        id = "budget",
                        label = "Approximate Total Budget (USD) *",
                        placeholder = "Enter budget amount",
                        min = 1,
                        isRequired = true,
                        errorMessage = "Budget must be a positive number"
                    },
                    new
                    {
                        type = "TextBlock",
                        text = "* Required fields",
                        size = "Small",
                        color = "Attention",
                        spacing = "Medium"
                    }
                },
                actions = new object[]
                {
                    new
                    {
                        type = "Action.Submit",
                        title = "Create Project",
                        style = "positive",
                        associatedInputs = "auto",
                        data = new
                        {
                            action = "submitProjectForm",
                            msteams = new
                            {
                                type = "messageBack",
                                displayText = "Create Project"
                            }
                        }
                    },
                    new
                    {
                        type = "Action.Submit",
                        title = "Cancel",
                        style = "destructive",
                        associatedInputs = "auto",
                        data = new
                        {
                            action = "cancelProjectForm",
                            msteams = new
                            {
                                type = "messageBack",
                                displayText = "Cancel"
                            }
                        }
                    }
                }
            };
        }
        */
    }
}