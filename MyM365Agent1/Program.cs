using MyM365Agent1;
using MyM365Agent1.Actions;
using MyM365Agent1.Model;
using MyM365Agent1.Services;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Azure.Blobs;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Models;
using Microsoft.Teams.AI.AI.Planners;
using Microsoft.Teams.AI.AI.Prompts;
using Microsoft.Teams.AI.AI;
using Azure.Identity;
using Azure.Core;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient("WebClient", client => client.Timeout = TimeSpan.FromSeconds(600));
builder.Services.AddHttpContextAccessor();

// Register External API Service
builder.Services.AddHttpClient<IExternalApiService, ExternalApiService>();

// Register named HttpClient for Secure API Service
builder.Services.AddHttpClient("SecureApiService", client => 
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Register Secure API Service with Managed Identity
builder.Services.AddScoped<ISecureApiServiceHttpClient, SecureApiServiceHttpClient>();

// Register workflow actions
builder.Services.AddScoped<CreateSourcingProjectAction>();
builder.Services.AddScoped<UpsertMilestonesAction>();
builder.Services.AddScoped<FindSuppliersAction>();
builder.Services.AddScoped<ShowSuppliersAction>();
builder.Services.AddScoped<SelectSuppliersAction>();
builder.Services.AddScoped<PublishProjectAction>();
builder.Services.AddScoped<ConfirmPublishAction>();
builder.Services.AddScoped<ResetWorkflowAction>();

// Register adaptive card actions (kept for compatibility but not imported to AI)
// Form-related actions - commented out
// builder.Services.AddScoped<ShowProjectFormAction>();
// builder.Services.AddScoped<SubmitProjectFormAction>();
// builder.Services.AddScoped<CancelProjectFormAction>();

// Register workflow orchestrator
builder.Services.AddScoped<WorkflowOrchestrator>();

// Register GraphQL Service
builder.Services.AddHttpClient<IGraphQLService, GraphQLService>();

// Register Supplier Recommendation Service
builder.Services.AddHttpClient<SupplierRecommendationService>();

// Prepare Configuration for ConfigurationBotFrameworkAuthentication
var config = builder.Configuration.Get<ConfigOptions>();
builder.Configuration["MicrosoftAppType"] = config.BOT_TYPE;
builder.Configuration["MicrosoftAppId"] = config.BOT_ID;
builder.Configuration["MicrosoftAppPassword"] = config.BOT_PASSWORD;
builder.Configuration["MicrosoftAppTenantId"] = config.BOT_TENANT_ID;
// Create the Bot Framework Authentication to be used with the Bot Adapter.
builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();

// Create the Cloud Adapter with error handling enabled.
// Note: some classes expect a BotAdapter and some expect a BotFrameworkHttpAdapter, so
// register the same adapter instance for both types.
builder.Services.AddSingleton<TeamsAdapter, AdapterWithErrorHandler>();
builder.Services.AddSingleton<IBotFrameworkHttpAdapter>(sp => sp.GetService<TeamsAdapter>());
builder.Services.AddSingleton<BotAdapter>(sp => sp.GetService<TeamsAdapter>());

// Configure Azure Blob Storage for durable state persistence
var storageConnectionString = builder.Configuration["StateStorage:ConnectionString"] 
    ?? builder.Configuration.GetConnectionString("StateStorage") 
    ?? builder.Configuration["AzureWebJobsStorage"];
var storageContainerName = builder.Configuration.GetValue<string>("StateStorage:ContainerName", "bot-state");

// Debug configuration values
Console.WriteLine($"🔧 Configuration Debug:");
Console.WriteLine($"   StateStorage:ConnectionString = {(string.IsNullOrEmpty(builder.Configuration["StateStorage:ConnectionString"]) ? "NULL/EMPTY" : "CONFIGURED")}");
Console.WriteLine($"   ConnectionStrings:StateStorage = {(string.IsNullOrEmpty(builder.Configuration.GetConnectionString("StateStorage")) ? "NULL/EMPTY" : "CONFIGURED")}");
Console.WriteLine($"   AzureWebJobsStorage = {(string.IsNullOrEmpty(builder.Configuration["AzureWebJobsStorage"]) ? "NULL/EMPTY" : "CONFIGURED")}");
Console.WriteLine($"   StateStorage:ContainerName = {storageContainerName}");

Console.WriteLine($"🔧 Storage Configuration: ConnectionString={(!string.IsNullOrEmpty(storageConnectionString) ? "Configured" : "None")}, Container={storageContainerName}");

if (!string.IsNullOrEmpty(storageConnectionString))
{
    // Use Azure Blob Storage for production - direct BlobsStorage for maximum compatibility
    var blobStorage = new BlobsStorage(storageConnectionString, storageContainerName);
    builder.Services.AddSingleton<IStorage>(blobStorage);
    
    Console.WriteLine($"✅ Using Azure Blob Storage for state persistence: {storageContainerName}");
    
    // Extract and log storage account name
    try
    {
        var parts = storageConnectionString.Split(';');
        var accountNamePart = parts.FirstOrDefault(p => p.StartsWith("AccountName="));
        var accountName = accountNamePart?.Split('=')[1] ?? "Unknown";
        Console.WriteLine($"✅ Storage Account: {accountName}");
    }
    catch
    {
        Console.WriteLine("✅ Storage Account: Unknown");
    }
}
else
{
    // Fallback to MemoryStorage for development/testing
    builder.Services.AddSingleton<IStorage, MemoryStorage>();
    Console.WriteLine("⚠️  Using MemoryStorage - state will not persist between app restarts");
}

// Add state management services
builder.Services.AddSingleton<Microsoft.Bot.Builder.UserState>();
builder.Services.AddSingleton<Microsoft.Bot.Builder.ConversationState>();

builder.Services.AddSingleton<OpenAIModel>(sp => 
{
    var loggerFactory = sp.GetService<ILoggerFactory>();
    
    // Prefer API key auth if a key is configured; otherwise fall back to Azure Identity
    var preferApiKey = !string.IsNullOrWhiteSpace(config.Azure?.OpenAIApiKey);
    
    if (!preferApiKey && config.Azure.UseAzureIdentity)
    {
        // Create appropriate Azure credential based on configuration
        TokenCredential credential;
        
        if (!string.IsNullOrEmpty(config.Azure.ClientId) && !string.IsNullOrEmpty(config.Azure.ClientSecret))
        {
            // Use Service Principal authentication with specific tenant
            credential = new ClientSecretCredential(
                config.Azure.TenantId ?? "72f988bf-86f1-41af-91ab-2d7cd011db47", // Default to Microsoft tenant
                config.Azure.ClientId,
                config.Azure.ClientSecret
            );
        }
        else
        {
            // Use DefaultAzureCredential with specific tenant if provided
            var options = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrEmpty(config.Azure.TenantId))
            {
                options.TenantId = config.Azure.TenantId;
            }
            credential = new DefaultAzureCredential(options);
        }

        return new OpenAIModel(
            new AzureOpenAIModelOptions(
                credential,
                config.Azure.OpenAIDeploymentName,
                config.Azure.OpenAIEndpoint
            )
            {
                LogRequests = true,
            },
            loggerFactory
        );
    }
    else
    {
        // Use API Key authentication when provided (default for local/dev)
        return new OpenAIModel(
            new AzureOpenAIModelOptions(
                config.Azure.OpenAIApiKey,
                config.Azure.OpenAIDeploymentName,
                config.Azure.OpenAIEndpoint
            )
            {
                LogRequests = true,
            },
            loggerFactory
        );
    }
});

// Create the bot as transient. In this case the ASP Controller is expecting an IBot.
builder.Services.AddTransient<IBot>(sp =>
{
    // Create loggers
    ILoggerFactory loggerFactory = sp.GetService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger<Program>();

    // Create Prompt Manager
    PromptManager prompts = new(new()
    {
        PromptFolder = "./Prompts"
    });

    // Create ActionPlanner
    ActionPlanner<AppState> planner = new(
        options: new(
            model: sp.GetService<OpenAIModel>(),
            prompts: prompts,
            defaultPrompt: async (context, state, planner) =>
            {
                PromptTemplate template = prompts.GetPrompt("planner");
                return await Task.FromResult(template);
            }
        )
        { LogRepairs = true },
        loggerFactory: loggerFactory
    );
    
    AIOptions<AppState> options = new(planner);
    options.EnableFeedbackLoop = true;

    Application<AppState> app = new ApplicationBuilder<AppState>()
        .WithAIOptions(options)
        .WithStorage(sp.GetService<IStorage>())
        .Build();

    // Get the workflow orchestrator and configure actions
    var orchestrator = sp.GetService<WorkflowOrchestrator>();
    orchestrator.ConfigureApplicationActions(app);

    // COMMENTED OUT: Handle Adaptive Card Action.Submit submissions BEFORE AI processing
    // app.OnActivity("message", async (turnContext, turnState, cancellationToken) =>
    // {
    //     try
    //     {
    //         // Diagnostics for incoming activities
    //         logger.LogInformation("[Message] type={Type}, text='{Text}', hasValue={HasValue}",
    //             turnContext.Activity.Type, turnContext.Activity.Text ?? "", turnContext.Activity.Value != null);

    //         // 1) Handle Adaptive Card submissions FIRST, even if Activity.Text has button title
    //         if (turnContext.Activity.Value != null)
    //         {
    //             logger.LogInformation("🎯 Adaptive Card Action.Submit detected - Processing form submission");
    //             Console.WriteLine("🎯 Adaptive Card Action.Submit detected - Processing form submission");

    //             // Parse the form data from Activity.Value
    //             var formData = JsonSerializer.Deserialize<Dictionary<string, object>>(
    //                 JsonSerializer.Serialize(turnContext.Activity.Value));

    //             if (formData != null)
    //             {
    //                 logger.LogInformation("Form data received: {FormData}", JsonSerializer.Serialize(formData));
    //                 Console.WriteLine($"Form data received: {JsonSerializer.Serialize(formData)}");

    //                 // Check if this contains an action identifier
    //                 if (formData.ContainsKey("action"))
    //                 {
    //                     var actionName = formData["action"]?.ToString();
    //                     logger.LogInformation("Action identified: {ActionName}", actionName);
    //                     Console.WriteLine($"Action identified: {actionName}");

    //                     // Route to appropriate action handler using captured service provider
    //                     switch (actionName)
    //                     {
    //                         case "submitProjectForm":
    //                             var submitAction = sp.GetService<SubmitProjectFormAction>();
    //                             if (submitAction != null)
    //                             {
    //                                 // Remove action identifier and pass form data as parameters
    //                                 formData.Remove("action");
    //                                 await submitAction.ExecuteAsync(turnContext, turnState, formData);
                                    
    //                                 // Mark the activity as handled to prevent further processing
    //                                 logger.LogInformation("✅ Form submission processed successfully - setting activity text to prevent AI processing");
    //                                 Console.WriteLine("✅ Form submission processed successfully - setting activity text to prevent AI processing");
                                    
    //                                 // Clear the activity text to prevent AI processing
    //                                 turnContext.Activity.Text = ""; 
    //                                 turnContext.Activity.Value = null;
    //                                 return; // Return to complete the handler
    //                             }
    //                             else
    //                             {
    //                                 logger.LogError("SubmitProjectFormAction service not found");
    //                                 Console.WriteLine("❌ SubmitProjectFormAction service not found");
    //                             }
    //                             break;

    //                         case "cancelProjectForm":
    //                             logger.LogInformation("✅ Form cancellation processed - user cancelled project creation");
    //                             Console.WriteLine("✅ Form cancellation processed - user cancelled project creation");
    //                             await turnContext.SendActivityAsync("Project creation cancelled. You can start over anytime by asking me to create a new project.");
                                
    //                             // Clear the activity to prevent AI processing
    //                             turnContext.Activity.Text = "";
    //                             turnContext.Activity.Value = null;
    //                             return; // Return to complete the handler

    //                         default:
    //                             logger.LogWarning("Unknown action type: {ActionName}", actionName);
    //                             Console.WriteLine($"⚠️ Unknown action type: {actionName}");
    //                             break;
    //                     }
    //                 }
    //                 else
    //                 {
    //                     logger.LogInformation("Form submission without action identifier");
    //                     Console.WriteLine("📝 Form submission without action identifier");
    //                 }
    //             }
    //         }

    //         // 2) Fast-path: intercept simple text commands and bypass LLM to avoid delays
    //         if (!string.IsNullOrWhiteSpace(turnContext.Activity.Text))
    //         {
    //             var text = turnContext.Activity.Text.Trim();
    //             var hasValue = turnContext.Activity.Value != null;

    //             // Only treat button titles specially when they come from a button click (hasValue=true)
    //             var isButtonTitle = text.Equals("Create Project", StringComparison.OrdinalIgnoreCase) ||
    //                                 text.Equals("Cancel", StringComparison.OrdinalIgnoreCase);

    //             if (isButtonTitle && hasValue)
    //             {
    //                 logger.LogInformation("Skipping fast-path for button click title: {Text}", text);
    //             }
    //             else if (Regex.IsMatch(text, @"\b(create|new)\s+project\b", RegexOptions.IgnoreCase))
    //             {
    //                 var showFormAction = sp.GetService<ShowProjectFormAction>();
    //                 if (showFormAction != null)
    //                 {
    //                     await showFormAction.ExecuteAsync(turnContext, turnState, new Dictionary<string, object>());
    //                     // Prevent further AI processing
    //                     turnContext.Activity.Text = string.Empty;
    //                     turnContext.Activity.Value = null;
    //                     return;
    //                 }
    //             }
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         logger.LogError(ex, "Error handling Adaptive Card Action.Submit");
    //         Console.WriteLine($"❌ Error handling Adaptive Card Action.Submit: {ex.Message}");
    //     }
    // });

    // COMMENTED OUT: Handle Teams 'invoke' activity for Adaptive Card submissions (adaptiveCard/action)
    // app.OnActivity("invoke", async (turnContext, turnState, cancellationToken) =>
    // {
    //     try
    //     {
    //         var name = turnContext.Activity.Name ?? string.Empty;
    //         if (!name.Equals("adaptiveCard/action", StringComparison.OrdinalIgnoreCase))
    //         {
    //             return; // let other invoke types flow
    //         }

    //         logger.LogInformation("🎯 Invoke: adaptiveCard/action detected - parsing submission payload");
    //         logger.LogInformation("[Invoke] hasValue={HasValue}", turnContext.Activity.Value != null);

    //         var valueJson = JsonSerializer.Serialize(turnContext.Activity.Value);
    //         using var doc = JsonDocument.Parse(valueJson);
    //         var root = doc.RootElement;

    //         Dictionary<string, object> payload = null;
    //         string actionName = null;

    //         // Preferred: value.action.data
    //         if (root.TryGetProperty("action", out var actionEl) && actionEl.ValueKind == JsonValueKind.Object)
    //         {
    //             if (actionEl.TryGetProperty("data", out var dataEl) && dataEl.ValueKind != JsonValueKind.Null)
    //             {
    //                 payload = JsonSerializer.Deserialize<Dictionary<string, object>>(dataEl.GetRawText());
    //             }
    //         }

    //         // Fallbacks: value.data or raw value as the payload
    //         if (payload == null)
    //         {
    //             if (root.TryGetProperty("data", out var altDataEl) && altDataEl.ValueKind != JsonValueKind.Null)
    //             {
    //                 payload = JsonSerializer.Deserialize<Dictionary<string, object>>(altDataEl.GetRawText());
    //             }
    //             else
    //             {
    //                 payload = JsonSerializer.Deserialize<Dictionary<string, object>>(valueJson);
    //             }
    //         }

    //         if (payload != null && payload.TryGetValue("action", out var actObj) && actObj != null)
    //         {
    //             actionName = actObj.ToString();
    //         }

    //         // Route based on our custom action field
    //         bool looksLikeProjectForm = payload != null &&
    //             (payload.ContainsKey("projectTitle") || payload.ContainsKey("description") || payload.ContainsKey("email"));

    //         if (string.Equals(actionName, "submitProjectForm", StringComparison.OrdinalIgnoreCase) || looksLikeProjectForm)
    //         {
    //             var submitAction = sp.GetService<SubmitProjectFormAction>();
    //             if (submitAction != null)
    //             {
    //                 payload.Remove("action");
    //                 await submitAction.ExecuteAsync(turnContext, turnState, payload);

    //                 // Clear activity to avoid further processing
    //                 turnContext.Activity.Text = string.Empty;
    //                 turnContext.Activity.Value = null;

    //                 // Acknowledge invoke to client
    //                 await turnContext.SendActivityAsync(new Activity
    //                 {
    //                     Type = ActivityTypesEx.InvokeResponse,
    //                     Value = new InvokeResponse { Status = 200 }
    //                 });
    //             }
    //             else
    //             {
    //                 logger.LogError("SubmitProjectFormAction service not found for invoke handling");
    //             }
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         logger.LogError(ex, "Error handling adaptiveCard/action invoke");
    //     }
    // });

    app.OnConversationUpdate("membersAdded", async (turnContext, turnState, cancellationToken) =>
    {
        // Initialize user state if needed - use safe access
        WorkflowStep currentStep;
        try
        {
            currentStep = turnState.User.CurrentStep;
        }
        catch (InvalidCastException ex)
        {
            // If there's a casting issue, log it but don't automatically reset
            logger.LogError(ex, "❌ Invalid workflow step stored in state during conversation update");
            logger.LogInformation("🔧 Letting improved CurrentStep getter handle the casting issue");
            currentStep = turnState.User.CurrentStep; // This will now use the improved getter
        }
        
        if (currentStep == default || currentStep == 0)
        {
            turnState.User.CurrentStep = WorkflowStep.PROJECT_TO_BE_CREATED;
            turnState.User.LastActivityTime = DateTime.UtcNow;
            currentStep = WorkflowStep.PROJECT_TO_BE_CREATED;
        }

        // Validate and repair workflow state
        currentStep = orchestrator.ValidateAndRepairWorkflowState(turnState);
        
        // Get appropriate welcome message
        var welcomeText = orchestrator.GetWelcomeMessage(currentStep, turnState.User.EmailId, turnState.User.ProjectId);
        
        foreach (var member in turnContext.Activity.MembersAdded)
        {
            if (member.Id != turnContext.Activity.Recipient.Id)
            {
                await turnContext.SendActivityAsync(MessageFactory.Text(welcomeText), cancellationToken);
            }
        }
    });

    // Remove the custom OnActivity handler - let ActionPlanner handle messages automatically
    
    // Add state initialization before AI processing
    app.OnBeforeTurn(async (turnContext, turnState, cancellationToken) =>
    {
        // Initialize user state if needed
        try
        {
            // Debug: Check raw state data before invalidating cache
            Console.WriteLine($"🔍 OnBeforeTurn DEBUG - TurnContext User ID: {turnContext.Activity?.From?.Id ?? "null"}");
            Console.WriteLine($"🔍 OnBeforeTurn DEBUG - TurnContext Channel ID: {turnContext.Activity?.ChannelId ?? "null"}");
            Console.WriteLine($"🔍 OnBeforeTurn DEBUG - TurnContext Conversation ID: {turnContext.Activity?.Conversation?.Id ?? "null"}");
            
            // CRITICAL: Check what's in state BEFORE invalidating cache
            Console.WriteLine($"🔍 OnBeforeTurn - BEFORE cache invalidation - about to invalidate cache");
            
            // Invalidate CurrentStep cache to ensure fresh read from storage
            turnState.User.InvalidateCurrentStepCache();
            
            Console.WriteLine($"🔍 OnBeforeTurn - AFTER cache invalidation, about to read CurrentStep");
            var currentStep = turnState.User.CurrentStep;
            Console.WriteLine($"🔍 OnBeforeTurn - CurrentStep read as: {currentStep}");
            
            // Log current state for debugging - moved after reading CurrentStep to see any errors
            logger.LogInformation("🔍 OnBeforeTurn - Current state: CurrentStep={CurrentStep}, ProjectId={ProjectId}, EngagementId={EngagementId}, StateId={StateId}",
                currentStep, turnState.User.ProjectId ?? "null", turnState.User.EngagementId ?? "null", turnState.User.StateId ?? "null");
            
            // Only initialize if we have no valid state data at all
            if (currentStep == WorkflowStep.PROJECT_TO_BE_CREATED && 
                string.IsNullOrEmpty(turnState.User.ProjectId) && 
                string.IsNullOrEmpty(turnState.User.EngagementId) &&
                string.IsNullOrEmpty(turnState.User.EmailId))
            {
                Console.WriteLine($"🔍 OnBeforeTurn - Initializing new state (no existing data)");
                turnState.User.CurrentStep = WorkflowStep.PROJECT_TO_BE_CREATED;
                turnState.User.LastActivityTime = DateTime.UtcNow;
                logger.LogInformation("Initialized user workflow state to PROJECT_TO_BE_CREATED - no existing data found");
            }
            else
            {
                Console.WriteLine($"🔍 OnBeforeTurn - Existing state found, NOT initializing");
                logger.LogInformation("📊 Existing state found - CurrentStep: {CurrentStep}, ProjectId: {ProjectId}, EngagementId: {EngagementId}", 
                    currentStep, turnState.User.ProjectId ?? "null", turnState.User.EngagementId ?? "null");
            }
        }
        catch (InvalidCastException ex)
        {
            logger.LogError(ex, "❌ InvalidCastException when reading CurrentStep - this indicates a serialization issue");
            // DON'T automatically reset - let the improved CurrentStep getter handle it
            logger.LogInformation("🔧 Letting improved CurrentStep getter handle the casting issue");
        }
        
        // Validate and repair workflow state with improved logic
        orchestrator.ValidateAndRepairWorkflowState(turnState);
        
        logger.LogInformation("Processing message in workflow step: {CurrentStep}", turnState.User.CurrentStep);
        
        return await Task.FromResult(true);
    });

    // Add state tracking after turn processing
    app.OnAfterTurn(async (turnContext, turnState, cancellationToken) =>
    {
        // Add detailed debugging
        Console.WriteLine($"💾 OnAfterTurn - About to save state:");
        Console.WriteLine($"💾   CurrentStep: {turnState.User.CurrentStep} (int: {(int)turnState.User.CurrentStep})");
        Console.WriteLine($"💾   ProjectId: {turnState.User.ProjectId ?? "null"}");
        Console.WriteLine($"💾   EngagementId: {turnState.User.EngagementId ?? "null"}");
        Console.WriteLine($"💾   EmailId: {turnState.User.EmailId ?? "null"}");
        Console.WriteLine($"💾   StateId: {turnState.User.StateId ?? "null"}");
        Console.WriteLine($"💾   TurnContext User ID: {turnContext.Activity?.From?.Id ?? "null"}");
        
        logger.LogInformation("💾 OnAfterTurn - Final state: CurrentStep={CurrentStep}, ProjectId={ProjectId}, EngagementId={EngagementId}",
            turnState.User.CurrentStep, turnState.User.ProjectId ?? "null", turnState.User.EngagementId ?? "null");
        
        return await Task.FromResult(true);
    });
    
    // Keep the existing feedback loop
    app.OnFeedbackLoop((turnContext, turnState, feedbackLoopData, _) =>
    {
        logger.LogInformation("Feedback received: {Feedback}", turnContext.Activity.Value?.ToString());
        return Task.CompletedTask;
    });

    // Handle reset command
    app.OnMessage("/reset", async (turnContext, turnState, cancellationToken) =>
    {
        turnState.User.CurrentStep = WorkflowStep.PROJECT_TO_BE_CREATED;
        turnState.User.EmailId = string.Empty;
        turnState.User.ProjectId = string.Empty;
        turnState.User.EngagementId = string.Empty;
        turnState.User.LastError = string.Empty;
        turnState.User.LastActivityTime = DateTime.UtcNow;
        turnState.User.StateId = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        // API response history removed to prevent serialization issues
        
        await turnContext.SendActivityAsync(MessageFactory.Text("🔄 Workflow reset! I'm ready to help you create a new sourcing project."), cancellationToken);
        logger.LogInformation("Workflow reset via /reset command");
    });

    // Add debug command to check current state
    app.OnMessage("/status", async (turnContext, turnState, cancellationToken) =>
    {
        var currentStep = turnState.User.CurrentStep;
        var stepDescription = currentStep switch
        {
            WorkflowStep.PROJECT_TO_BE_CREATED => "Ready to create a new sourcing project",
            WorkflowStep.PROJECT_CREATED => "Project created, ready to add milestones",
            WorkflowStep.MILESTONES_CREATED => "Milestones added, ready for supplier selection",
            WorkflowStep.SUPPLIERS_SELECTED => "Suppliers selected, ready for publishing",
            WorkflowStep.PUBLISHED => "Project published and complete",
            WorkflowStep.Error => "Error state - workflow needs to be reset",
            _ => "Unknown state"
        };

        var statusText = $"📊 **Current Workflow Status:**\n\n" +
            $"• **Current Step:** {currentStep}\n" +
            $"• **Step Description:** {stepDescription}\n\n" +
            $"📋 **Project Details:**\n" +
            $"• **Project ID:** {turnState.User.ProjectId ?? "None"}\n" +
            $"• **Engagement ID:** {turnState.User.EngagementId ?? "None"}\n" +
            $"• **Email ID:** {turnState.User.EmailId ?? "None"}\n\n" +
            $"🔧 **Debug Info:**\n" +
            $"• **State ID:** {turnState.User.StateId ?? "None"}\n" +
            $"• **Last Activity:** {turnState.User.LastActivityTime}\n" +
            $"• **Last Error:** {turnState.User.LastError ?? "None"}";
            // API History Count removed to prevent serialization issues

        await turnContext.SendActivityAsync(MessageFactory.Text(statusText), cancellationToken);
        logger.LogInformation("Status check requested via /status command");
    });

    // API history debug command removed to prevent serialization issues

    // COMMENTED OUT: Explicit message routes for create/new project to always show the form
    // app.OnMessage("create project", async (turnContext, turnState, cancellationToken) =>
    // {
    //     var showFormAction = sp.GetService<ShowProjectFormAction>();
    //     if (showFormAction != null)
    //     {
    //         await showFormAction.ExecuteAsync(turnContext, turnState, new Dictionary<string, object>());
    //     }
    // });

    // app.OnMessage("new project", async (turnContext, turnState, cancellationToken) =>
    // {
    //     var showFormAction = sp.GetService<ShowProjectFormAction>();
    //     if (showFormAction != null)
    //     {
    //         await showFormAction.ExecuteAsync(turnContext, turnState, new Dictionary<string, object>());
    //     }
    // });

    // NEW: Text-based project creation flow
    app.OnMessage("create project", async (turnContext, turnState, cancellationToken) =>
    {
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

        await turnContext.SendActivityAsync(MessageFactory.Text(promptText), cancellationToken);
    });

    app.OnMessage("new project", async (turnContext, turnState, cancellationToken) =>
    {
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

        await turnContext.SendActivityAsync(MessageFactory.Text(promptText), cancellationToken);
    });

    // COMMENTED OUT: Add direct test handler for form display
    // app.OnMessage("/testform", async (turnContext, turnState, cancellationToken) =>
    // {
    //     try
    //     {
    //         logger.LogInformation("🧪 Direct test of ShowProjectFormAction triggered");
    //         Console.WriteLine("🧪 Direct test of ShowProjectFormAction triggered");
            
    //         var showFormAction = sp.GetService<ShowProjectFormAction>();
    //         if (showFormAction != null)
    //         {
    //             await showFormAction.ExecuteAsync(turnContext, turnState, new Dictionary<string, object>());
    //             logger.LogInformation("✅ ShowProjectFormAction executed successfully via direct test");
    //             Console.WriteLine("✅ ShowProjectFormAction executed successfully via direct test");
    //         }
    //         else
    //         {
    //             logger.LogError("❌ ShowProjectFormAction service not found in direct test");
    //             Console.WriteLine("❌ ShowProjectFormAction service not found in direct test");
    //             await turnContext.SendActivityAsync("❌ Form action service not found");
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         logger.LogError(ex, "❌ Error in direct form test");
    //         Console.WriteLine($"❌ Error in direct form test: {ex.Message}");
    //         await turnContext.SendActivityAsync($"❌ Error testing form: {ex.Message}");
    //     }
    // });

    return app;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseRouting();
app.MapControllers();

app.Run();