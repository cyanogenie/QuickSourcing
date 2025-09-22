using Microsoft.Bot.Builder;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Action;
using MyM365Agent1.Model;
using MyM365Agent1.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyM365Agent1.Actions
{
    /// <summary>
    /// Action to save selected suppliers after user has made their choice
    /// </summary>
    public class SelectSuppliersAction
    {
        private readonly IGraphQLService _graphQLService;
        private readonly ILogger<SelectSuppliersAction> _logger;

        public SelectSuppliersAction(IGraphQLService graphQLService, ILogger<SelectSuppliersAction> logger)
        {
            _graphQLService = graphQLService;
            _logger = logger;
        }

        [Action("selectSuppliers")]
        public async Task<string> ExecuteAsync(
            [ActionTurnContext] ITurnContext turnContext, 
            [ActionTurnState] AppState state, 
            [ActionParameters] Dictionary<string, object> parameters)
        {
            try
            {
                _logger.LogInformation("SelectSuppliersAction triggered");

                // Only proceed if suppliers have been found
                if (state.User.CurrentStep != WorkflowStep.SUPPLIERS_FOUND)
                {
                    await turnContext.SendActivityAsync("I can only save supplier selections after suppliers have been found. Please find suppliers first.");
                    return "Action not available in current workflow step";
                }

                // Get project details from state
                var projectId = state.User.ProjectId;
                var engagementId = state.User.EngagementId;

                if (string.IsNullOrEmpty(projectId))
                {
                    _logger.LogError("Missing project ID for supplier selection");
                    await turnContext.SendActivityAsync("I'm missing the project ID needed to save supplier selections. Please try creating the project again.");
                    return "Missing project ID";
                }

                // Extract supplier selection from user input
                var input = parameters.ContainsKey("input") ? parameters["input"]?.ToString() : turnContext.Activity.Text ?? "";
                _logger.LogInformation("Processing supplier selection input: {Input}", input);

                var selectedSuppliers = ExtractSelectedSuppliers(input, state);
                
                if (selectedSuppliers.Count == 0)
                {
                    await turnContext.SendActivityAsync("I couldn't identify which suppliers you want to select. Please specify using their Order ID numbers (e.g., 'Select supplier #1 and #2' or 'Choose suppliers 1, 2').");
                    return "No suppliers identified in selection";
                }

                _logger.LogInformation("Selected {Count} suppliers for project {ProjectId}", selectedSuppliers.Count, projectId);

                // Build GraphQL mutation
                var suppliersList = string.Join(",", selectedSuppliers.Select(s => 
                    $"{{vendorNumber:\"{s.VendorNumber}\",companyCode:\"{s.CompanyCode}\"}}"));

                var mutation = $@"
                mutation {{
                    upsertProjectSuppliers(
                        input: {{
                            projectId: {projectId},
                            suppliersList: [{suppliersList}]
                        }}
                    ) {{
                        projectId
                    }}
                }}";

                _logger.LogInformation("GraphQL mutation: {Mutation}", mutation);

                // Send progress message
                await turnContext.SendActivityAsync($"💾 Saving your selection of {selectedSuppliers.Count} supplier(s)...");

                try
                {
                    // Convert to GraphQLService SelectedSupplier format
                    var graphqlSuppliers = selectedSuppliers.Select(s => new MyM365Agent1.Services.SelectedSupplier
                    {
                        VendorName = s.VendorName,
                        VendorNumber = s.VendorNumber,
                        CompanyCode = s.CompanyCode,
                        Location = "", // Add location from the original supplier data if needed
                        Rating = 0, // Add rating from the original supplier data if needed
                        Status = "Selected"
                    }).ToList();

                    // Call GraphQL API to save selected suppliers
                    var response = await _graphQLService.UpsertProjectSuppliersAsync(
                        projectId, 
                        graphqlSuppliers);

                    _logger.LogInformation("Supplier selection API response: {Response}", response);

                    // Parse response
                    var responseData = JsonSerializer.Deserialize<JsonElement>(response);
                    
                    if (responseData.TryGetProperty("data", out var dataElement) &&
                        dataElement.TryGetProperty("upsertProjectSuppliers", out var resultElement) &&
                        resultElement.TryGetProperty("projectId", out var returnedProjectId))
                    {
                        var parsedData = new Dictionary<string, object>
                        {
                            ["projectId"] = returnedProjectId.GetInt32().ToString(),
                            ["selectedSuppliersCount"] = selectedSuppliers.Count,
                            ["selectedSuppliers"] = selectedSuppliers.Select(s => new SelectedSupplierData { VendorNumber = s.VendorNumber, CompanyCode = s.CompanyCode, VendorName = s.VendorName }).ToList()
                        };

                        // API response history storage removed to prevent serialization issues

                        // Update workflow state to SUPPLIERS_SELECTED 
                        state.User.CurrentStep = WorkflowStep.SUPPLIERS_SELECTED;
                        _logger.LogInformation("🔄 State updated: CurrentStep=SUPPLIERS_SELECTED, ProjectId={ProjectId}", projectId);

                        // Build success response
                        var successMessage = BuildSuccessMessage(selectedSuppliers, projectId);
                        await turnContext.SendActivityAsync(successMessage);

                        // Guide user to the next step - let AI automatically trigger publishProject
                        await turnContext.SendActivityAsync("🚀 **Preparing your project for publication...**");

                        _logger.LogInformation("✅ Supplier selection completed successfully");
                        return $"Successfully saved {selectedSuppliers.Count} supplier selections for project {projectId}. Ready for automatic publication review.";
                    }
                    else
                    {
                        _logger.LogError("Invalid response structure from supplier selection API");
                        await turnContext.SendActivityAsync("❌ I received an unexpected response from the supplier selection API. Please try again.");
                        return "Invalid API response structure";
                    }
                }
                catch (Exception apiEx)
                {
                    _logger.LogError(apiEx, "Error calling supplier selection API");
                    
                    // Store failed response
                    var errorData = new Dictionary<string, object>
                    {
                        ["projectId"] = projectId,
                        ["selectedSuppliersCount"] = selectedSuppliers.Count,
                        ["error"] = apiEx.Message
                    };
                    
                    // API response history storage removed to prevent serialization issues

                    await turnContext.SendActivityAsync($"❌ I encountered an error while saving your supplier selection: {apiEx.Message}. Please try again.");
                    return $"API call failed: {apiEx.Message}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SelectSuppliersAction");
                await turnContext.SendActivityAsync("❌ I encountered an error while processing your supplier selection. Please try again.");
                return $"Action failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Extract selected suppliers from user input and match with stored supplier data
        /// </summary>
        private List<SelectedSupplier> ExtractSelectedSuppliers(string input, AppState state)
        {
            var selectedSuppliers = new List<SelectedSupplier>();

            try
            {
                // Get supplier data from simple state property
                var suppliersJson = state.User.SuppliersJson;
                if (string.IsNullOrEmpty(suppliersJson))
                {
                    _logger.LogWarning("No stored supplier data found for selection");
                    return selectedSuppliers;
                }

                // Parse the stored JSON string
                using var document = JsonDocument.Parse(suppliersJson);
                var responseData = document.RootElement;
                if (!responseData.TryGetProperty("results", out var resultsProperty))
                {
                    _logger.LogWarning("No results found in stored supplier data");
                    return selectedSuppliers;
                }

                // Extract order IDs from user input
                var orderIds = ExtractOrderIds(input);
                _logger.LogInformation("Extracted order IDs from input: {OrderIds}", string.Join(", ", orderIds));

                // Match order IDs with supplier data
                foreach (var supplier in resultsProperty.EnumerateArray())
                {
                    if (supplier.TryGetProperty("currentOrder", out var orderProperty))
                    {
                        var currentOrder = orderProperty.GetInt32();
                        if (orderIds.Contains(currentOrder))
                        {
                            var vendorNumber = supplier.TryGetProperty("vendorNumber", out var vnProperty) 
                                ? vnProperty.GetString() : "";
                            var companyCode = supplier.TryGetProperty("companyCode", out var ccProperty) 
                                ? ccProperty.GetString() : "1010"; // Default to 1010
                            var vendorName = supplier.TryGetProperty("vendorName", out var nameProperty) 
                                ? nameProperty.GetString() : "";

                            if (!string.IsNullOrEmpty(vendorNumber))
                            {
                                selectedSuppliers.Add(new SelectedSupplier
                                {
                                    OrderId = currentOrder,
                                    VendorNumber = vendorNumber,
                                    CompanyCode = companyCode,
                                    VendorName = vendorName?.Trim()
                                });
                                _logger.LogInformation("Matched supplier: Order {OrderId}, Vendor {VendorNumber}, Name {VendorName}", 
                                    currentOrder, vendorNumber, vendorName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting selected suppliers");
            }

            return selectedSuppliers;
        }

        /// <summary>
        /// Extract order IDs from user input using various patterns
        /// </summary>
        private List<int> ExtractOrderIds(string input)
        {
            var orderIds = new List<int>();

            // Pattern 1: "supplier #1", "supplier #2"
            var hashPattern = @"#(\d+)";
            var hashMatches = Regex.Matches(input, hashPattern, RegexOptions.IgnoreCase);
            foreach (Match match in hashMatches)
            {
                if (int.TryParse(match.Groups[1].Value, out var id))
                    orderIds.Add(id);
            }

            // Pattern 2: "supplier 1", "supplier 2", "suppliers 1, 2"
            var numberPattern = @"supplier\s*(\d+)|(\d+)(?=\s*(?:and|,|\s|$))";
            var numberMatches = Regex.Matches(input, numberPattern, RegexOptions.IgnoreCase);
            foreach (Match match in numberMatches)
            {
                var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (int.TryParse(value, out var id) && !orderIds.Contains(id))
                    orderIds.Add(id);
            }

            // Pattern 3: Simple comma-separated numbers "1, 2, 3"
            var simplePattern = @"\b(\d+)\b";
            var simpleMatches = Regex.Matches(input, simplePattern);
            foreach (Match match in simpleMatches)
            {
                if (int.TryParse(match.Groups[1].Value, out var id) && !orderIds.Contains(id))
                    orderIds.Add(id);
            }

            return orderIds.Distinct().OrderBy(x => x).ToList();
        }

        /// <summary>
        /// Build success message for supplier selection
        /// </summary>
        private string BuildSuccessMessage(List<SelectedSupplier> selectedSuppliers, string projectId)
        {
            var message = "🎯 **Supplier Selection Saved Successfully!**\n\n";
            message += "┌─────────────────────────────────────────────────────────────┐\n";
            message += "│                👥 **SELECTED SUPPLIERS**                    │\n";
            message += "└─────────────────────────────────────────────────────────────┘\n\n";
            message += $"🆔 **Project ID:** `{projectId}`\n";
            message += $"📊 **Total Selected:** `{selectedSuppliers.Count} suppliers`\n\n";

            message += "**📋 Your Selected Suppliers:**\n";
            foreach (var supplier in selectedSuppliers.OrderBy(s => s.OrderId))
            {
                message += $"✅ **#{supplier.OrderId}** - {supplier.VendorName} (`{supplier.VendorNumber}`)\n";
            }

            message += "\n─────────────────────────────────────────────────────────────\n";
            message += "\n🚀 **Ready for Publication!**\n";
            message += "• Your selected suppliers have been saved to the project\n";
            message += "• Project is now ready to be published to these suppliers\n";
            message += "• Suppliers will receive RFX notifications via the portal\n";

            return message;
        }

        /// <summary>
        /// Helper class to represent a selected supplier
        /// </summary>
        private class SelectedSupplier
        {
            public int OrderId { get; set; }
            public string VendorNumber { get; set; } = "";
            public string CompanyCode { get; set; } = "";
            public string VendorName { get; set; } = "";
        }
    }

    /// <summary>
    /// Simple data class for selected supplier information to avoid serialization issues with anonymous types
    /// </summary>
    public class SelectedSupplierData
    {
        public string VendorNumber { get; set; } = "";
        public string CompanyCode { get; set; } = "";
        public string VendorName { get; set; } = "";
    }
}