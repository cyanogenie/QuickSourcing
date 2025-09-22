# Clear Bot State Script
# This script helps clear the Azure Blob Storage state that contains problematic anonymous types

Write-Host "🧹 Bot State Cleanup Script" -ForegroundColor Cyan
Write-Host "This script will help clear the problematic bot state from Azure Blob Storage." -ForegroundColor Yellow
Write-Host ""

# Get connection string from appsettings
$settingsPath = ".\appsettings.Playground.json"
if (Test-Path $settingsPath) {
    $settings = Get-Content $settingsPath | ConvertFrom-Json
    $connectionString = $settings.StateStorage.ConnectionString
    $containerName = $settings.StateStorage.ContainerName
    
    Write-Host "Found settings:" -ForegroundColor Green
    Write-Host "  Container: $containerName" -ForegroundColor Gray
    Write-Host "  Connection: [CONFIGURED]" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "⚠️  WARNING: This will delete ALL bot state data!" -ForegroundColor Red
    Write-Host "   - User workflow progress will be lost" -ForegroundColor Red
    Write-Host "   - All conversation history will be cleared" -ForegroundColor Red
    Write-Host "   - API response cache will be removed" -ForegroundColor Red
    Write-Host ""
    
    $confirm = Read-Host "Are you sure you want to proceed? (yes/no)"
    
    if ($confirm -eq "yes" -or $confirm -eq "y") {
        Write-Host ""
        Write-Host "🔧 To clear the state, you have these options:" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "1. Azure Storage Explorer:" -ForegroundColor Yellow
        Write-Host "   - Download Azure Storage Explorer" -ForegroundColor Gray
        Write-Host "   - Connect using the connection string" -ForegroundColor Gray
        Write-Host "   - Navigate to Blob Containers > $containerName" -ForegroundColor Gray
        Write-Host "   - Delete all blobs in the container" -ForegroundColor Gray
        Write-Host ""
        Write-Host "2. Azure CLI:" -ForegroundColor Yellow
        Write-Host "   az storage blob delete-batch --source $containerName --account-name quicksourcing" -ForegroundColor Gray
        Write-Host ""
        Write-Host "3. Azure Portal:" -ForegroundColor Yellow
        Write-Host "   - Go to Azure Portal > Storage Account > quicksourcing" -ForegroundColor Gray
        Write-Host "   - Navigate to Containers > $containerName" -ForegroundColor Gray
        Write-Host "   - Select all blobs and delete them" -ForegroundColor Gray
        Write-Host ""
        Write-Host "After clearing the state:" -ForegroundColor Green
        Write-Host "✅ The serialization error will be resolved" -ForegroundColor Green
        Write-Host "✅ Users can start fresh workflows" -ForegroundColor Green
        Write-Host "✅ The bot will use the new string-based ParsedData format" -ForegroundColor Green
    } else {
        Write-Host "Operation cancelled." -ForegroundColor Yellow
    }
} else {
    Write-Host "❌ Could not find $settingsPath" -ForegroundColor Red
    Write-Host "Please run this script from the project directory." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")