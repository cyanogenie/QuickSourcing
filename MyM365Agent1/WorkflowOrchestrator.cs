using Microsoft.Bot.Builder;
using Microsoft.Teams.AI;
using MyM365Agent1.Actions;
using MyM365Agent1.Model;

namespace MyM365Agent1
{
    /// <summary>
    /// State-driven workflow orchestrator that manages which actions are available
    /// based on the user's current workflow step
    /// </summary>
    public class WorkflowOrchestrator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<WorkflowOrchestrator> _logger;

        public WorkflowOrchestrator(
            IServiceProvider serviceProvider,
            ILogger<WorkflowOrchestrator> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Configure the application with workflow actions
        /// Since Teams AI doesn't allow dynamic action addition, we add all actions but control logic within them
        /// </summary>
        public void ConfigureApplicationActions(Application<AppState> app)
        {
            _logger.LogInformation("Configuring workflow actions on application");

            // Import active workflow actions into the application
            // The state-based logic will be handled within each action's ExecuteAsync method
            var createSourcingProjectAction = _serviceProvider.GetRequiredService<CreateSourcingProjectAction>();
            var upsertMilestonesAction = _serviceProvider.GetRequiredService<UpsertMilestonesAction>();
            var resetWorkflowAction = _serviceProvider.GetRequiredService<ResetWorkflowAction>();

            app.AI.ImportActions(createSourcingProjectAction);
            app.AI.ImportActions(upsertMilestonesAction);
            app.AI.ImportActions(resetWorkflowAction);

            _logger.LogInformation("✅ Active workflow actions imported: createSourcingProject, upsertMilestones, resetWorkflow");
            Console.WriteLine("✅ Active workflow actions imported: createSourcingProject, upsertMilestones, resetWorkflow");
        }

        /// <summary>
        /// Get appropriate welcome message based on current workflow step
        /// </summary>
        public string GetWelcomeMessage(WorkflowStep currentStep, string emailId = "", string projectId = "")
        {
            return currentStep switch
            {
                WorkflowStep.PROJECT_TO_BE_CREATED => @"Hello! I'm here to help you create a sourcing project. 

🚀 **To get started, say 'create project' or 'new project'** and I'll guide you through providing the necessary details.

You can provide project information in a natural way, like:
""Create a project called 'Website Redesign' with description 'Update company website for better UX', email john@company.com, start date 2025-01-01, budget 25000""

Or just say **'create project'** and I'll show you the format!",
                
                WorkflowStep.PROJECT_CREATED => !string.IsNullOrEmpty(projectId)
                    ? $"Welcome back! Your sourcing project (ID: {projectId}) has been created. Now let's add milestones and deliverables to complete your project setup."
                    : "Your project has been created. Let's add milestones and deliverables to complete the setup.",
                    
                WorkflowStep.Error => @"I encountered an error in our previous interaction. Let's start fresh. 

🚀 **Say 'create project'** and I'll guide you through creating a new sourcing project with text-based inputs.",
                
                _ => @"Hello! I'm your sourcing assistant. I can help you create sourcing projects and manage project milestones. 

🚀 **Say 'create project'** to get started!"
            };
        }

        /// <summary>
        /// Check if the current workflow state is valid and repair if needed
        /// </summary>
        public WorkflowStep ValidateAndRepairWorkflowState(AppState state)
        {
            var currentStep = state.User.CurrentStep;
            var emailId = state.User.EmailId;
            var projectId = state.User.ProjectId;

            _logger.LogInformation("🔍 ValidateAndRepairWorkflowState - CurrentStep: {CurrentStep}, EmailId: '{EmailId}', ProjectId: '{ProjectId}'", 
                currentStep, emailId ?? "null", projectId ?? "null");

            // Validate state consistency and repair if needed
            switch (currentStep)
            {
                case WorkflowStep.PROJECT_CREATED:
                    // Be more lenient - only reset if BOTH email AND project are missing
                    if (string.IsNullOrEmpty(emailId) && string.IsNullOrEmpty(projectId))
                    {
                        _logger.LogWarning("🔧 PROJECT_CREATED step but BOTH email and project ID are missing, resetting to PROJECT_TO_BE_CREATED");
                        currentStep = WorkflowStep.PROJECT_TO_BE_CREATED;
                        state.User.CurrentStep = currentStep;
                        state.User.EmailId = string.Empty;
                        state.User.ProjectId = string.Empty;
                        state.User.EngagementId = string.Empty;
                    }
                    else
                    {
                        _logger.LogInformation("✅ PROJECT_CREATED state validation passed - At least one of EmailId or ProjectId is present");
                        
                        // If only one is missing, try to recover from API history
                        if (string.IsNullOrEmpty(emailId) || string.IsNullOrEmpty(projectId))
                        {
                            var apiResponse = state.User.GetApiResponse("createSourcingProject");
                            if (apiResponse != null && apiResponse.IsSuccess)
                            {
                                if (string.IsNullOrEmpty(emailId))
                                {
                                    var recoveredEmail = state.User.GetApiResponseValue<string>("createSourcingProject", "emailId");
                                    if (!string.IsNullOrEmpty(recoveredEmail))
                                    {
                                        state.User.EmailId = recoveredEmail;
                                        _logger.LogInformation("🔧 Recovered EmailId from API history: {EmailId}", recoveredEmail);
                                    }
                                }
                                if (string.IsNullOrEmpty(projectId))
                                {
                                    var recoveredProjectId = state.User.GetApiResponseValue<string>("createSourcingProject", "projectId");
                                    if (!string.IsNullOrEmpty(recoveredProjectId))
                                    {
                                        state.User.ProjectId = recoveredProjectId;
                                        _logger.LogInformation("🔧 Recovered ProjectId from API history: {ProjectId}", recoveredProjectId);
                                    }
                                }
                            }
                        }
                    }
                    break;
                    
                case WorkflowStep.MILESTONES_CREATED:
                    // Be more lenient here too
                    if (string.IsNullOrEmpty(emailId) && string.IsNullOrEmpty(projectId))
                    {
                        _logger.LogWarning("🔧 MILESTONES_CREATED step but BOTH email and project ID are missing, resetting to PROJECT_TO_BE_CREATED");
                        currentStep = WorkflowStep.PROJECT_TO_BE_CREATED;
                        state.User.CurrentStep = currentStep;
                        state.User.EmailId = string.Empty;
                        state.User.ProjectId = string.Empty;
                        state.User.EngagementId = string.Empty;
                    }
                    else
                    {
                        _logger.LogInformation("✅ MILESTONES_CREATED state validation passed - At least one of EmailId or ProjectId is present");
                    }
                    break;
                    
                default:
                    _logger.LogInformation("ℹ️ No validation needed for CurrentStep: {CurrentStep}", currentStep);
                    break;
            }

            _logger.LogInformation("🔚 ValidateAndRepairWorkflowState result - CurrentStep: {CurrentStep}", currentStep);
            return currentStep;
        }

        /// <summary>
        /// Get context message to guide the AI planner based on current workflow step
        /// </summary>
        public string GetPlannerContext(WorkflowStep currentStep, string emailId = "", string projectId = "")
        {
            return currentStep switch
            {
                WorkflowStep.PROJECT_TO_BE_CREATED => "The user is at the beginning of the sourcing workflow. They need to provide project details (title, description, email, dates, budget) to create a sourcing project. Available actions: createSourcingProject, resetWorkflow.",
                
                WorkflowStep.PROJECT_CREATED => "The user has created a sourcing project and now needs to add milestones and deliverables. Available actions: upsertMilestones, resetWorkflow.",
                
                WorkflowStep.Error => "An error occurred in the workflow. The user can restart or try again. Available actions: createSourcingProject, resetWorkflow.",
                
                _ => "The user can interact with the sourcing system. Help them get started by creating a sourcing project."
            };
        }
    }
}