# Azure OpenAI Authentication Configuration

## Issue Resolution

The error "AuthenticationTypeDisabled: Key based authentication is disabled for this resource" occurs when your Azure OpenAI resource is configured to use Azure Active Directory (AAD) authentication instead of API keys.

## Solution Implemented

The project now supports both authentication methods:

### 1. Azure Active Directory Authentication (Recommended)
- Uses `DefaultAzureCredential` from Azure.Identity
- More secure and follows Azure best practices
- Required when key-based authentication is disabled

### 2. API Key Authentication (Fallback)
- Uses traditional API key authentication
- Available when key-based authentication is enabled

## Configuration

In your `appsettings.json` or `appsettings.Development.json`:

```json
{
  "Azure": {
    "OpenAIApiKey": "",
    "OpenAIEndpoint": "https://your-openai-resource.openai.azure.com/",
    "OpenAIDeploymentName": "your-deployment-name",
    "UseAzureIdentity": true
  }
}
```

### Configuration Options:

- **UseAzureIdentity**: `true` (use AAD auth) or `false` (use API key)
- **OpenAIEndpoint**: Your Azure OpenAI endpoint URL
- **OpenAIDeploymentName**: Your model deployment name
- **OpenAIApiKey**: Only needed when `UseAzureIdentity` is `false`

## Authentication Methods

### Azure Identity (AAD) Authentication
When `UseAzureIdentity` is `true`, the app uses `DefaultAzureCredential` which tries:

1. **Environment variables** (AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID)
2. **Managed Identity** (when running in Azure)
3. **Visual Studio** (when developing locally)
4. **Azure CLI** (when developing locally)
5. **Azure PowerShell** (when developing locally)

### Local Development Setup

For local development with Azure Identity, ensure you're signed in via:

#### Option 1: Azure CLI
```bash
az login
az account set --subscription "your-subscription-id"
```

#### Option 2: Visual Studio
- Sign in to Visual Studio with your Azure account
- Go to Tools > Options > Azure Service Authentication

#### Option 3: Environment Variables
```bash
$env:AZURE_CLIENT_ID = "your-client-id"
$env:AZURE_CLIENT_SECRET = "your-client-secret"  
$env:AZURE_TENANT_ID = "your-tenant-id"
```

## Azure RBAC Permissions

Ensure your user/service principal has the required permissions on the Azure OpenAI resource:

### Required Role Assignment:
- **Cognitive Services OpenAI User** or
- **Cognitive Services OpenAI Contributor**

### To assign permissions:
1. Go to your Azure OpenAI resource in Azure Portal
2. Navigate to "Access control (IAM)"
3. Click "Add role assignment"
4. Select the appropriate role
5. Assign to your user or application

## Testing the Configuration

1. Update your `appsettings.Development.json` with your Azure OpenAI endpoint and deployment name
2. Set `UseAzureIdentity` to `true`
3. Ensure you're authenticated (via Azure CLI, Visual Studio, etc.)
4. Run the application and test

## Troubleshooting

### Common Issues:

1. **"DefaultAzureCredential failed to retrieve a token"**
   - Ensure you're signed in via Azure CLI or Visual Studio
   - Check RBAC permissions on the Azure OpenAI resource

2. **"Resource not found"**
   - Verify the OpenAI endpoint URL is correct
   - Verify the deployment name matches your Azure OpenAI deployment

3. **"Insufficient permissions"**
   - Ensure proper RBAC role assignment
   - Check if your account has access to the subscription/resource group

### Debug Information:
- Enable detailed logging by setting log level to "Debug" in appsettings
- Check application logs for authentication details
- Verify endpoint and deployment name configuration

## Fallback to API Key

If you need to use API key authentication temporarily:

1. Set `UseAzureIdentity` to `false`
2. Provide the `OpenAIApiKey` in configuration
3. Ensure key-based authentication is enabled on your Azure OpenAI resource
