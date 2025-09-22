using Microsoft.Bot.Builder;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Action;
using MyM365Agent1.Model;
using System.ComponentModel;
using System.Text.Json;
using System.Text;

namespace MyM365Agent1.Actions
{
    /// <summary>
    /// Action to display previously found suppliers to the user
    /// </summary>
    public class ShowSuppliersAction
    {
        private readonly ILogger<ShowSuppliersAction> _logger;

        public ShowSuppliersAction(ILogger<ShowSuppliersAction> logger)
        {
            _logger = logger;
        }

        [Action("showSuppliers")]
        [Description("Show the list of recommended suppliers that were previously found")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext, 
            [ActionTurnState] AppState state, 
            [ActionParameters] Dictionary<string, object> parameters)
        {
            try
            {
                _logger.LogInformation("ShowSuppliersAction triggered");
                _logger.LogInformation("Current workflow step: {CurrentStep}", state.User.CurrentStep);

                // Check if suppliers have been found - suppliers should only be shown after they've been found
                if (state.User.CurrentStep < WorkflowStep.SUPPLIERS_FOUND)
                {
                    _logger.LogWarning("User attempting to show suppliers at step {CurrentStep}, but suppliers not found yet", state.User.CurrentStep);
                    
                    if (state.User.CurrentStep < WorkflowStep.MILESTONES_CREATED)
                    {
                        await turnContext.SendActivityAsync("❌ Suppliers are not available yet. Please complete your project setup and add milestones first before searching for suppliers.");
                        return "Milestones not completed - suppliers not available";
                    }
                    else
                    {
                        await turnContext.SendActivityAsync("❌ No suppliers have been found yet. Please run a supplier search first by saying 'find suppliers' or 'search for suppliers'.");
                        return "No suppliers available to display";
                    }
                }

                // If suppliers are already selected, show them with selected status
                if (state.User.CurrentStep >= WorkflowStep.SUPPLIERS_SELECTED)
                {
                    _logger.LogInformation("Suppliers already selected - showing selected suppliers list");
                    
                    // Check if we have selected suppliers data
                    if (!string.IsNullOrEmpty(state.User.SuppliersJson))
                    {
                        try
                        {
                            await turnContext.SendActivityAsync("✅ **Your Selected Suppliers**\n\nHere are the suppliers you have selected for this project:");
                            
                            // Parse and display selected suppliers
                            var selectedData = JsonSerializer.Deserialize<JsonElement>(state.User.SuppliersJson);
                            var selectedSupplierDisplay = FormatSelectedSuppliers(selectedData);
                            await turnContext.SendActivityAsync(selectedSupplierDisplay);
                            
                            // Add status message based on current step
                            var statusMessage = state.User.CurrentStep switch
                            {
                                WorkflowStep.SUPPLIERS_SELECTED => 
                                    "\n🚀 **Next Step:** Say 'publish project' to review publication details and proceed.",
                                WorkflowStep.PUBLISHED => 
                                    "\n🎉 **Project Status:** Published! Suppliers can now respond via the Supplier Web Portal.",
                                _ => 
                                    "\n✅ **Status:** Suppliers confirmed for this project."
                            };
                            
                            await turnContext.SendActivityAsync(statusMessage);
                            return "Selected suppliers displayed successfully";
                        }
                        catch (JsonException)
                        {
                            _logger.LogWarning("Failed to parse selected suppliers JSON, falling back to status message");
                        }
                    }
                    
                    // Fallback if no selected suppliers data available
                    var fallbackMessage = state.User.CurrentStep switch
                    {
                        WorkflowStep.SUPPLIERS_SELECTED => 
                            "✅ **Suppliers Already Selected!**\n\n" +
                            "Your project suppliers have been confirmed and saved. " +
                            "Your project is now ready for publication.\n\n" +
                            "🚀 **Next Step:** Say 'publish project' to review publication details and proceed.",
                        WorkflowStep.PUBLISHED => 
                            "🎉 **Project Already Published!**\n\n" +
                            "Your sourcing project has been successfully published to the selected suppliers. " +
                            "Suppliers can now view the project details and submit their proposals via the Supplier Web Portal.\n\n" +
                            "✅ **Status:** Live and accepting supplier responses",
                        _ => 
                            "✅ **Suppliers Previously Selected**\n\n" +
                            "Suppliers have been selected for this project. Use other actions to manage your project."
                    };

                    await turnContext.SendActivityAsync(fallbackMessage);
                    return "Supplier status displayed - already selected";
                }

                // Get suppliers from state
                var suppliersJson = state.User.SuppliersJson;
                
                if (string.IsNullOrEmpty(suppliersJson))
                {
                    await turnContext.SendActivityAsync("❌ No supplier data available. Please run a supplier search to find suppliers.");
                    return "No supplier data stored";
                }

                _logger.LogInformation("Displaying suppliers from stored data");

                try
                {
                    // Parse the stored supplier data
                    var responseData = JsonSerializer.Deserialize<JsonElement>(suppliersJson);
                    
                    // Format and display suppliers
                    var supplierDisplay = FormatSuppliersData(responseData);
                    await turnContext.SendActivityAsync(supplierDisplay);

                    // Add helper message for supplier selection (only for SUPPLIERS_FOUND state)
                    if (state.User.CurrentStep == WorkflowStep.SUPPLIERS_FOUND)
                    {
                        await turnContext.SendActivityAsync("💡 **Ready to select suppliers?** You can say 'Select supplier #1 and #3' or 'Choose suppliers 2, 4, 5' to make your selection.");
                    }

                    _logger.LogInformation("✅ Suppliers displayed successfully");
                    return "Suppliers displayed successfully";
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "Error parsing stored supplier data");
                    await turnContext.SendActivityAsync("❌ Error reading supplier data. Please run a new supplier search.");
                    return "Error parsing supplier data";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ShowSuppliersAction");
                await turnContext.SendActivityAsync("❌ I encountered an error while displaying suppliers. Please try again.");
                return $"Action failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Format the supplier data for user display as a table
        /// (Same logic as FindSuppliersAction but extracted for reuse)
        /// </summary>
                private string FormatSelectedSuppliers(JsonElement selectedData)
        {
            var formatted = new StringBuilder();
            
            try
            {
                var suppliers = selectedData.GetProperty("suppliers").EnumerateArray();
                var count = 0;
                
                foreach (var supplier in suppliers)
                {
                    count++;
                    var companyName = supplier.GetProperty("companyName").GetString();
                    var location = supplier.GetProperty("location").GetString();
                    
                    // Parse rating with proper decimal handling
                    var ratingText = "N/A";
                    if (supplier.TryGetProperty("feedbackRating", out var ratingElement))
                    {
                        if (ratingElement.ValueKind == JsonValueKind.Number)
                        {
                            var rating = ratingElement.GetDecimal();
                            ratingText = $"{rating:F1}/5.0";
                        }
                        else if (ratingElement.ValueKind == JsonValueKind.String)
                        {
                            var ratingStr = ratingElement.GetString();
                            if (decimal.TryParse(ratingStr, out var parsedRating))
                            {
                                ratingText = $"{parsedRating:F1}/5.0";
                            }
                        }
                    }
                    
                    // Parse cost rating
                    var costRatingText = "N/A";
                    if (supplier.TryGetProperty("costRating", out var costElement))
                    {
                        if (costElement.ValueKind == JsonValueKind.Number)
                        {
                            var costRating = costElement.GetInt32();
                            costRatingText = costRating switch
                            {
                                5 => "Very High",
                                4 => "High",
                                3 => "Medium",
                                2 => "Low",
                                1 => "Very Low",
                                _ => "N/A"
                            };
                        }
                        else if (costElement.ValueKind == JsonValueKind.String)
                        {
                            costRatingText = costElement.GetString() ?? "N/A";
                        }
                    }
                    
                    // Parse experience level
                    var experienceText = "N/A";
                    if (supplier.TryGetProperty("experienceLevel", out var expElement))
                    {
                        if (expElement.ValueKind == JsonValueKind.Number)
                        {
                            var expLevel = expElement.GetInt32();
                            experienceText = expLevel switch
                            {
                                5 => "Expert",
                                4 => "Advanced",
                                3 => "Intermediate",
                                2 => "Basic",
                                1 => "Entry Level",
                                _ => "N/A"
                            };
                        }
                        else if (expElement.ValueKind == JsonValueKind.String)
                        {
                            experienceText = expElement.GetString() ?? "N/A";
                        }
                    }
                    
                    formatted.AppendLine($"**{count}. {companyName}** ✅ *Selected*");
                    formatted.AppendLine($"   📍 **Location:** {location}");
                    formatted.AppendLine($"   ⭐ **Rating:** {ratingText}");
                    formatted.AppendLine($"   💰 **Cost Rating:** {costRatingText}");
                    formatted.AppendLine($"   🎯 **Experience:** {experienceText}");
                    formatted.AppendLine();
                }
                
                if (count == 0)
                {
                    return "No selected suppliers found in the saved data.";
                }
                
                return formatted.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error formatting selected suppliers");
                return "Error displaying selected suppliers. Please try again.";
            }
        }

        private string FormatSuppliersData(JsonElement responseData)
        {
            try
            {
                var message = "📋 **Available Recommended Suppliers**\n\n";
                
                // Check if the response contains results
                if (responseData.TryGetProperty("count", out var countProperty) && 
                    responseData.TryGetProperty("results", out var resultsProperty) && 
                    resultsProperty.GetArrayLength() > 0)
                {
                    var count = countProperty.ValueKind == JsonValueKind.Number ? countProperty.GetInt32() : resultsProperty.GetArrayLength();
                    message += $"🏢 **{count} Suppliers Available for Selection**\n\n";
                    
                    // Create table header with Order ID
                    message += "| **#** | **Supplier** | **Location** | **Experience** | **Cost Rating** | **Rating** | **Status** |\n";
                    message += "|-------|-------------|-------------|---------------|----------------|-----------|----------|\n";
                    
                    foreach (var supplier in resultsProperty.EnumerateArray())
                    {
                        // Extract key supplier information for table display with proper null handling
                        var currentOrder = supplier.TryGetProperty("currentOrder", out var orderProperty) 
                            ? GetSafeStringValue(orderProperty, "N/A") 
                            : "N/A";
                            
                        var vendorName = supplier.TryGetProperty("vendorName", out var nameProperty) 
                            ? GetSafeStringValue(nameProperty, "Unknown") 
                            : (supplier.TryGetProperty("companyName", out var companyProperty) 
                                ? GetSafeStringValue(companyProperty, "Unknown") 
                                : "Unknown");
                        
                        var location = supplier.TryGetProperty("country", out var countryProperty) 
                            ? GetSafeStringValue(countryProperty, "Unknown") 
                            : (supplier.TryGetProperty("location", out var locationProperty) 
                                ? GetSafeStringValue(locationProperty, "Unknown") 
                                : "Unknown");
                        
                        // Extract experience level using the same logic as FindSuppliersAction
                        var experienceLevel = supplier.TryGetProperty("experienceLevel", out var expProperty) 
                            && expProperty.ValueKind == JsonValueKind.Number
                            ? expProperty.GetInt32() : 0;
                        
                        var experience = experienceLevel switch
                        {
                            5 => "Very High",
                            4 => "High", 
                            3 => "Medium",
                            2 => "Low",
                            1 => "Very Low",
                            _ => "N/A"
                        };
                        
                        // Extract cost rating using the same logic as FindSuppliersAction
                        var costRatingValue = supplier.TryGetProperty("costRating", out var costProperty) 
                            && costProperty.ValueKind == JsonValueKind.Number
                            ? costProperty.GetInt32() : 0;
                        
                        var costRating = costRatingValue switch
                        {
                            5 => "Very High",
                            4 => "High",
                            3 => "Medium", 
                            2 => "Low",
                            1 => "Very Low",
                            _ => "N/A"
                        };
                        
                        // Extract rating using the same logic as FindSuppliersAction
                        var feedbackRating = supplier.TryGetProperty("feedbackRating", out var ratingProperty) 
                            && ratingProperty.ValueKind == JsonValueKind.Number
                            ? ratingProperty.GetDecimal() : 0;
                        
                        var rating = feedbackRating > 0 ? $"{feedbackRating:F1}/5.0" : "N/A";
                        
                        var status = supplier.TryGetProperty("status", out var statusProperty) 
                            ? GetSafeStringValue(statusProperty, "Available") 
                            : "Available";
                        
                        // Add row to table with safe string extraction
                        message += $"| **{currentOrder}** | {vendorName} | {location} | {experience} | {costRating} | {rating} | {status} |\n";
                    }
                    
                    message += $"\n📊 **Total: {count} suppliers available**";
                }
                else if (responseData.TryGetProperty("results", out var emptyResults) && emptyResults.GetArrayLength() == 0)
                {
                    message += "❌ **No suppliers found matching your criteria.**\n\n";
                    message += "💡 You may want to try:\n";
                    message += "- Adjusting your project requirements\n";
                    message += "- Running a new supplier search\n";
                    message += "- Contacting procurement for additional options";
                }
                else
                {
                    message += "⚠️ **Unable to parse supplier data.**\n\n";
                    message += "The response format may have changed. Please try running a new supplier search.";
                    _logger.LogWarning("Unexpected supplier response format: {Response}", responseData.ToString());
                }
                
                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error formatting supplier response");
                return "❌ **Error displaying suppliers.** Please try running a new supplier search.";
            }
        }

        /// <summary>
        /// Safely extract string values from JsonElement with fallback
        /// </summary>
        private string GetSafeStringValue(JsonElement element, string fallback = "N/A")
        {
            try
            {
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString() ?? fallback,
                    JsonValueKind.Number => element.GetDecimal().ToString(),
                    JsonValueKind.True => "Yes",
                    JsonValueKind.False => "No",
                    JsonValueKind.Null => fallback,
                    _ => element.ToString() ?? fallback
                };
            }
            catch
            {
                return fallback;
            }
        }
    }
}