namespace MyM365Agent1
{
    public class ConfigOptions
    {
        public string BOT_ID { get; set; }
        public string BOT_PASSWORD { get; set; }
        public string BOT_TYPE { get; set; }
        public string BOT_TENANT_ID { get; set; }
        public AzureConfigOptions Azure { get; set; }
        public ExternalApiConfigOptions ExternalApi { get; set; }
    }

    /// <summary>
    /// Options for Azure OpenAI and Azure Content Safety
    /// </summary>
    public class AzureConfigOptions
    {
        public string OpenAIApiKey { get; set; }
        public string OpenAIEndpoint { get; set; }
        public string OpenAIDeploymentName { get; set; }
        public bool UseAzureIdentity { get; set; } = true;
        
        // Service Principal Authentication (Alternative to default credential)
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        
        // Subscription and Resource Group for scoped access
        public string SubscriptionId { get; set; }
        public string ResourceGroupName { get; set; }
    }

    /// <summary>
    /// Options for External API configuration
    /// </summary>
    public class ExternalApiConfigOptions
    {
        public string BaseUrl { get; set; }
        public string BearerToken { get; set; }
    }
}
