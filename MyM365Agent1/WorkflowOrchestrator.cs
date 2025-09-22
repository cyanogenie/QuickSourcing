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
            var findSuppliersAction = _serviceProvider.GetRequiredService<FindSuppliersAction>();
            var showSuppliersAction = _serviceProvider.GetRequiredService<ShowSuppliersAction>();
            var selectSuppliersAction = _serviceProvider.GetRequiredService<SelectSuppliersAction>();
            var publishProjectAction = _serviceProvider.GetRequiredService<PublishProjectAction>();
            var confirmPublishAction = _serviceProvider.GetRequiredService<ConfirmPublishAction>();
            var resetWorkflowAction = _serviceProvider.GetRequiredService<ResetWorkflowAction>();

            app.AI.ImportActions(createSourcingProjectAction);
            app.AI.ImportActions(upsertMilestonesAction);
            app.AI.ImportActions(findSuppliersAction);
            app.AI.ImportActions(showSuppliersAction);
            app.AI.ImportActions(selectSuppliersAction);
            app.AI.ImportActions(publishProjectAction);
            app.AI.ImportActions(confirmPublishAction);
            app.AI.ImportActions(resetWorkflowAction);

            _logger.LogInformation("✅ Active workflow actions imported: createSourcingProject, upsertMilestones, findSuppliers, showSuppliers, selectSuppliers, publishProject, confirmPublish, resetWorkflow");
            Console.WriteLine("✅ Active workflow actions imported: createSourcingProject, upsertMilestones, findSuppliers, showSuppliers, selectSuppliers, publishProject, confirmPublish, resetWorkflow");
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
                    
                WorkflowStep.MILESTONES_CREATED => !string.IsNullOrEmpty(projectId)
                    ? $"Great! Your project milestones have been set up (Project ID: {projectId}). Now let's find suitable suppliers for your project. Say 'find suppliers' to search for recommended suppliers."
                    : "Your project milestones have been created. Let's find suitable suppliers for your project. Say 'find suppliers' to continue.",
                    
                WorkflowStep.SUPPLIERS_FOUND => !string.IsNullOrEmpty(projectId)
                    ? $"Perfect! I've found suppliers for your project (ID: {projectId}). Please review the supplier table above and tell me which suppliers you'd like to select by providing their Order IDs (e.g., '1, 3, 5' or 'select suppliers 2 and 4'). You can also say 'show suppliers' to view them again."
                    : "Suppliers have been found for your project. Please review the supplier table and tell me which suppliers you'd like to select by providing their Order IDs. You can also say 'show suppliers' to view them again.",
                    
                WorkflowStep.SUPPLIERS_SELECTED => !string.IsNullOrEmpty(projectId)
                    ? $"Excellent! You've selected suppliers for your project (ID: {projectId}). Your supplier selections have been saved. You can say 'show suppliers' to review your selections or 'publish project' to make it available to suppliers."
                    : "Suppliers have been selected and saved for your project. You can say 'show suppliers' to review your selections or 'publish project' to make it available to suppliers.",
                
                WorkflowStep.PUBLISHED => !string.IsNullOrEmpty(projectId)
                    ? $"🎉 Congratulations! Your project (ID: {projectId}) has been successfully published and is now live! Suppliers can view the project details and submit their proposals. You can start a new project or monitor responses."
                    : "🎉 Your sourcing project has been successfully published! Suppliers can now submit proposals. You can start a new project or monitor responses.",
                
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
                        
                        // Simple validation - both should be available from state
                        if (string.IsNullOrEmpty(emailId) || string.IsNullOrEmpty(projectId))
                        {
                            _logger.LogWarning("⚠️ Missing critical project data - EmailId: {EmailExists}, ProjectId: {ProjectExists}", 
                                !string.IsNullOrEmpty(emailId), !string.IsNullOrEmpty(projectId));
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
                    
                case WorkflowStep.SUPPLIERS_SELECTED:
                    // Validate suppliers state
                    if (string.IsNullOrEmpty(emailId) && string.IsNullOrEmpty(projectId))
                    {
                        _logger.LogWarning("🔧 SUPPLIERS_SELECTED step but BOTH email and project ID are missing, resetting to PROJECT_TO_BE_CREATED");
                        currentStep = WorkflowStep.PROJECT_TO_BE_CREATED;
                        state.User.CurrentStep = currentStep;
                        state.User.EmailId = string.Empty;
                        state.User.ProjectId = string.Empty;
                        state.User.EngagementId = string.Empty;
                    }
                    else
                    {
                        _logger.LogInformation("✅ SUPPLIERS_SELECTED state validation passed - At least one of EmailId or ProjectId is present");
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
                
                WorkflowStep.MILESTONES_CREATED => "The user has created a project and added milestones. Now they need to find suppliers for their project. Available actions: findSuppliers, resetWorkflow.",
                
                WorkflowStep.SUPPLIERS_FOUND => "The user has found suppliers for their project. They can now select suppliers from the displayed table using Order IDs. Available actions: selectSuppliers, resetWorkflow.",
                
                WorkflowStep.SUPPLIERS_SELECTED => "The user has selected suppliers for their project. They can now publish the project to make it available to suppliers. Available actions: publishProject, confirmPublish, resetWorkflow.",
                
                WorkflowStep.PUBLISHED => "The user has successfully published their sourcing project to suppliers. The project is now live and suppliers can submit proposals. Available actions: resetWorkflow.",
                
                WorkflowStep.Error => "An error occurred in the workflow. The user can restart or try again. Available actions: createSourcingProject, resetWorkflow.",
                
                _ => "The user can interact with the sourcing system. Help them get started by creating a sourcing project."
            };
        }
    }
}