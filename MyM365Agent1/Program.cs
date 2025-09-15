using MyM365Agent1;
using MyM365Agent1.Model;
using MyM365Agent1.Services;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Models;
using Microsoft.Teams.AI.AI.Planners;
using Microsoft.Teams.AI.AI.Prompts;
using Microsoft.Teams.AI.AI;
using Azure.Identity;
using Azure.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient("WebClient", client => client.Timeout = TimeSpan.FromSeconds(600));
builder.Services.AddHttpContextAccessor();

// Register External API Service
builder.Services.AddHttpClient<IExternalApiService, ExternalApiService>();

// Register GraphQL Service
builder.Services.AddHttpClient<IGraphQLService, GraphQLService>();

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

builder.Services.AddSingleton<IStorage, MemoryStorage>();

builder.Services.AddSingleton<OpenAIModel>(sp => 
{
    var loggerFactory = sp.GetService<ILoggerFactory>();
    
    if (config.Azure.UseAzureIdentity)
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
        // Use API Key authentication (fallback)
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

    app.OnConversationUpdate("membersAdded", async (turnContext, turnState, cancellationToken) =>
    {
        var welcomeText = "How can I help you today?";
        foreach (var member in turnContext.Activity.MembersAdded)
        {
            if (member.Id != turnContext.Activity.Recipient.Id)
            {
                await turnContext.SendActivityAsync(MessageFactory.Text(welcomeText), cancellationToken);
            }
        }
    });

    app.AI.ImportActions(new ActionHandlers(sp.GetService<IExternalApiService>(), sp.GetService<IGraphQLService>(), sp.GetService<ILogger<ActionHandlers>>()));
    // Listen for user to say "/reset".
    app.OnMessage("/reset", ActivityHandlers.ResetMessageHandler);

    app.OnFeedbackLoop((turnContext, turnState, feedbackLoopData, _) =>
    {
        Console.WriteLine($"Your feedback is {turnContext.Activity.Value.ToString()}");
        return Task.CompletedTask;
    });

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