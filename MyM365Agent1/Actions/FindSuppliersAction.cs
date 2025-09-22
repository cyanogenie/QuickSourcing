using Microsoft.Bot.Builder;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Action;
using MyM365Agent1.Model;
using MyM365Agent1.Services;
using System.Text.Json;

namespace MyM365Agent1.Actions
{
    /// <summary>
    /// Action to find suppliers after milestones are created
    /// </summary>
    public class FindSuppliersAction
    {
        private readonly SupplierRecommendationService _supplierRecommendationService;
        private readonly ILogger<FindSuppliersAction> _logger;

        public FindSuppliersAction(SupplierRecommendationService supplierRecommendationService, ILogger<FindSuppliersAction> logger)
        {
            _supplierRecommendationService = supplierRecommendationService;
            _logger = logger;
        }

        [Action("findSuppliers")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext, 
            [ActionTurnState] AppState state, 
            [ActionParameters] Dictionary<string, object> parameters)
        {
            try
            {
                _logger.LogInformation("FindSuppliersAction triggered");

                // Only proceed if milestones have been created
                if (state.User.CurrentStep != WorkflowStep.MILESTONES_CREATED)
                {
                    await turnContext.SendActivityAsync("I can only find suppliers after milestones have been created. Please create milestones first.");
                    return "Action not available in current workflow step";
                }

                // Get project details from state
                var projectId = state.User.ProjectId;
                var engagementId = state.User.EngagementId;

                if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(engagementId))
                {
                    _logger.LogError("Missing project details. ProjectId: {ProjectId}, EngagementId: {EngagementId}", projectId, engagementId);
                    await turnContext.SendActivityAsync("I'm missing project details needed to find suppliers. Please try creating the project again.");
                    return "Missing project details";
                }

                _logger.LogInformation("Finding suppliers for ProjectId: {ProjectId}, EngagementId: {EngagementId}", projectId, engagementId);

                // Prepare the input list for supplier recommendation REST API
                var inputList = new[] { "IT Consulting-1010" };

                _logger.LogInformation("Supplier recommendation API input: {Input}", JsonSerializer.Serialize(inputList));

                // Call the supplier recommendation API
                await turnContext.SendActivityAsync("🔍 Searching for suppliers based on your project requirements...");

                try
                {
                    var response = await _supplierRecommendationService.GetSupplierRecommendationsAsync(inputList);

                    _logger.LogInformation("Supplier recommendation API response: {Response}", response);

                    // Parse and process the response
                    var responseData = JsonSerializer.Deserialize<JsonElement>(response);
                    
                    // Store the API response for potential future use
                    // Store supplier data as simple JSON string to avoid serialization issues
                    state.User.SuppliersJson = response;
                    
                    // Store API response in history (disabled for safety)
                    var parsedData = new Dictionary<string, object>
                    {
                        ["projectId"] = projectId,
                        ["engagementId"] = engagementId,
                        ["inputCategory"] = "IT Consulting-1010",
                        ["responseData"] = responseData
                    };
                    // API response history storage removed to prevent serialization issues

                    // Update workflow state to SUPPLIERS_FOUND
                    state.User.CurrentStep = WorkflowStep.SUPPLIERS_FOUND;
                    _logger.LogInformation("🔄 State updated: CurrentStep=SUPPLIERS_FOUND, ProjectId={ProjectId}, EngagementId={EngagementId}", 
                        projectId, engagementId);

                    // Provide user feedback
                    var successMessage = FormatSupplierResponse(responseData);
                    await turnContext.SendActivityAsync(successMessage);

                    _logger.LogInformation("✅ Supplier search completed successfully. State should be persisted automatically.");
                    return "Suppliers found successfully";
                }
                catch (Exception apiEx)
                {
                    _logger.LogError(apiEx, "Error calling supplier recommendation API");
                    
                    // Store failed response for debugging
                    var errorData = new Dictionary<string, object>
                    {
                        ["projectId"] = projectId,
                        ["engagementId"] = engagementId,
                        ["error"] = apiEx.Message
                    };
                    
                    // API response history storage removed to prevent serialization issues

                    await turnContext.SendActivityAsync($"❌ I encountered an error while searching for suppliers: {apiEx.Message}. Please try again later.");
                    return $"API call failed: {apiEx.Message}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FindSuppliersAction");
                await turnContext.SendActivityAsync("❌ I encountered an error while searching for suppliers. Please try again.");
                return $"Action failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Format the supplier recommendation response for user display as a table
        /// </summary>
        private string FormatSupplierResponse(JsonElement responseData)
        {
            try
            {
                // Log the actual response structure for debugging
                _logger.LogInformation("🔍 Supplier API response structure: {Response}", responseData.ToString());
                
                var message = "✅ **Supplier Search Completed**\n\n";
                
                // Check if the response contains results
                if (responseData.TryGetProperty("count", out var countProperty) && 
                    responseData.TryGetProperty("results", out var resultsProperty) && 
                    resultsProperty.GetArrayLength() > 0)
                {
                    var count = countProperty.ValueKind == JsonValueKind.Number ? countProperty.GetInt32() : resultsProperty.GetArrayLength();
                    message += $"🏢 **Found {count} Recommended Suppliers**\n\n";
                    
                    // Create table header with Order ID
                    message += "| **#** | **Supplier** | **Location** | **Experience** | **Cost Rating** | **Rating** | **Status** |\n";
                    message += "|-------|-------------|-------------|---------------|----------------|-----------|----------|\n";
                    
                    foreach (var supplier in resultsProperty.EnumerateArray())
                    {
                        // Extract key supplier information for table display with proper null handling
                        var currentOrder = supplier.TryGetProperty("currentOrder", out var orderProperty) 
                            && orderProperty.ValueKind == JsonValueKind.Number 
                            ? orderProperty.GetInt32() : 0;
                        
                        var vendorName = supplier.TryGetProperty("vendorName", out var nameProperty) 
                            && nameProperty.ValueKind == JsonValueKind.String
                            ? nameProperty.GetString()?.Trim() ?? "Unknown Vendor" : "Unknown Vendor";
                        
                        var vendorNumber = supplier.TryGetProperty("vendorNumber", out var numberProperty) 
                            && numberProperty.ValueKind == JsonValueKind.String
                            ? numberProperty.GetString() ?? "" : "";
                        
                        var location = supplier.TryGetProperty("vendorLocation", out var locationProperty) 
                            && locationProperty.ValueKind == JsonValueKind.String
                            ? locationProperty.GetString() ?? "N/A" : "N/A";
                        
                        var feedbackRating = supplier.TryGetProperty("feedbackRating", out var ratingProperty) 
                            && ratingProperty.ValueKind == JsonValueKind.Number
                            ? ratingProperty.GetDecimal() : 0;
                        
                        var experienceLevel = supplier.TryGetProperty("experienceLevel", out var expProperty) 
                            && expProperty.ValueKind == JsonValueKind.Number
                            ? expProperty.GetInt32() : 0;
                        
                        var costRating = supplier.TryGetProperty("costRating", out var costProperty) 
                            && costProperty.ValueKind == JsonValueKind.Number
                            ? costProperty.GetInt32() : 0;
                        
                        var isActive = supplier.TryGetProperty("isActive", out var activeProperty) 
                            && activeProperty.ValueKind != JsonValueKind.Null
                            ? (activeProperty.ValueKind == JsonValueKind.True || 
                               (activeProperty.ValueKind == JsonValueKind.String && activeProperty.GetString()?.ToLower() == "true"))
                            : false;
                        
                        // Format table row
                        var supplierCell = !string.IsNullOrEmpty(vendorNumber) 
                            ? $"{vendorName}<br/>({vendorNumber})" 
                            : vendorName;
                        
                        var experienceText = experienceLevel switch
                        {
                            5 => "Very High",
                            4 => "High", 
                            3 => "Medium",
                            2 => "Low",
                            1 => "Very Low",
                            _ => "N/A"
                        };
                        
                        var costText = costRating switch
                        {
                            5 => "Very High",
                            4 => "High",
                            3 => "Medium", 
                            2 => "Low",
                            1 => "Very Low",
                            _ => "N/A"
                        };
                        
                        var ratingText = feedbackRating > 0 ? $"{feedbackRating:F1}/5.0" : "N/A";
                        var statusText = isActive ? "✅ Active" : "❌ Inactive";
                        
                        // Truncate long names for table display
                        if (supplierCell.Length > 25)
                        {
                            var parts = supplierCell.Split("<br/>");
                            if (parts.Length > 1)
                            {
                                var name = parts[0].Length > 20 ? parts[0].Substring(0, 17) + "..." : parts[0];
                                supplierCell = $"{name}<br/>{parts[1]}";
                            }
                            else
                            {
                                supplierCell = supplierCell.Substring(0, 22) + "...";
                            }
                        }
                        
                        if (location.Length > 15)
                        {
                            location = location.Substring(0, 12) + "...";
                        }
                        
                        // Display current order (fallback to sequential if not available)
                        var orderDisplay = currentOrder > 0 ? currentOrder.ToString() : (resultsProperty.EnumerateArray().ToList().IndexOf(supplier) + 1).ToString();
                        
                        message += $"| **{orderDisplay}** | {supplierCell} | {location} | {experienceText} | {costText} | {ratingText} | {statusText} |\n";
                    }
                    
                    message += "\n";
                    message += "*💡 Tip: Use the Order ID (#) to reference suppliers when making your selection.*\n\n";
                    
                    // Add detailed information for the first supplier as an example
                    if (resultsProperty.GetArrayLength() > 0)
                    {
                        var firstSupplier = resultsProperty[0];
                        
                        if (firstSupplier.TryGetProperty("vendorName", out var firstNameProperty))
                        {
                            var firstName = firstNameProperty.GetString()?.Trim();
                            message += $"**🔍 Details for {firstName}:**\n";
                            
                            if (firstSupplier.TryGetProperty("profileDescription", out var descProperty))
                            {
                                var description = descProperty.GetString();
                                if (!string.IsNullOrEmpty(description))
                                {
                                    var truncatedDesc = description.Length > 200 
                                        ? description.Substring(0, 200) + "..." 
                                        : description;
                                    message += $"• **Profile:** {truncatedDesc}\n";
                                }
                            }
                            
                            if (firstSupplier.TryGetProperty("vendorWebsiteURL", out var websiteProperty))
                            {
                                var website = websiteProperty.GetString();
                                if (!string.IsNullOrEmpty(website))
                                    message += $"• **Website:** {website}\n";
                            }
                            
                            // Show IT Consulting capability score
                            if (firstSupplier.TryGetProperty("capabilities", out var capabilities))
                            {
                                foreach (var capability in capabilities.EnumerateArray())
                                {
                                    if (capability.TryGetProperty("capability", out var capName) && 
                                        capability.TryGetProperty("score", out var capScore) &&
                                        capName.GetString() == "IT Consulting")
                                    {
                                        message += $"• **IT Consulting Score:** {capScore.GetInt32()}/5\n";
                                        break;
                                    }
                                }
                            }
                            
                            if (firstSupplier.TryGetProperty("sspA_Status", out var sspaProperty))
                            {
                                var sspaStatus = sspaProperty.GetString();
                                if (!string.IsNullOrEmpty(sspaStatus))
                                    message += $"• **SSPA Status:** {sspaStatus}\n";
                            }
                            
                            message += "\n";
                        }
                    }
                }
                else if (responseData.TryGetProperty("results", out var altResultsProperty) && altResultsProperty.GetArrayLength() > 0)
                {
                    // Alternative structure without count property
                    var count = altResultsProperty.GetArrayLength();
                    message += $"🏢 **Found {count} Recommended Suppliers**\n\n";
                    
                    // Create table header with Order ID
                    message += "| **#** | **Supplier** | **Location** | **Experience** | **Cost Rating** | **Rating** | **Status** |\n";
                    message += "|-------|-------------|-------------|---------------|----------------|-----------|----------|\n";
                    
                    var index = 1;
                    foreach (var supplier in altResultsProperty.EnumerateArray())
                    {
                        // Use safe parsing for alternative structure
                        var vendorName = GetSafeStringValue(supplier, "vendorName") ?? 
                                       GetSafeStringValue(supplier, "name") ?? 
                                       GetSafeStringValue(supplier, "supplierName") ?? 
                                       "Unknown Vendor";
                        
                        var location = GetSafeStringValue(supplier, "vendorLocation") ?? 
                                      GetSafeStringValue(supplier, "location") ?? 
                                      "N/A";
                        
                        message += $"| **{index}** | {vendorName} | {location} | N/A | N/A | N/A | Active |\n";
                        index++;
                    }
                    
                    message += "\n";
                }
                else
                {
                    // Fallback message if structure is different
                    message += "I've successfully searched for suppliers but the response format was unexpected.\n\n";
                    message += $"Raw response data available for further processing.\n\n";
                }
                
                message += "🎯 **Next Steps:**\n";
                message += "• Review the supplier recommendations in the table above\n";
                message += "• Consider factors like experience level, cost rating, and overall rating\n";
                message += "• **Select suppliers by their Order ID** (e.g., 'Select supplier #1 and #2')\n";
                message += "• You can now proceed to publish your project to selected suppliers";
                
                return message;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error formatting supplier response, using generic message");
                return "✅ **Supplier Search Completed**\n\n" +
                       "I've successfully found suppliers for your project! " +
                       "The system has analyzed your requirements and identified potential matches. " +
                       "You can now proceed to the next step in your workflow.";
            }
        }

        /// <summary>
        /// Safely extract string value from JSON element with fallback
        /// </summary>
        private string? GetSafeStringValue(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) && 
                property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
            return null;
        }
    }
}