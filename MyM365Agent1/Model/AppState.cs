using Microsoft.Teams.AI.State;
using System.Text.Json;

namespace MyM365Agent1.Model
{
    /// <summary>
    /// Represents the different steps in our sourcing project workflow
    /// </summary>
    public enum WorkflowStep
    {
        PROJECT_TO_BE_CREATED,  // Order: 1 - Default state, sourcing project needs to be created
        PROJECT_CREATED,        // Order: 2 - Project created, now collecting milestone details
        MILESTONES_CREATED,     // Order: 3 - Milestones added, ready for supplier selection
        SUPPLIERS_FOUND,        // Order: 4 - Suppliers found, ready for selection
        SUPPLIERS_SELECTED,     // Order: 5 - Suppliers selected, ready for publishing
        PUBLISHED,              // Order: 6 - Project published and complete
        Error
    }

    // Extend the turn state by configuring custom strongly typed state classes.
    public class AppState : TurnState
    {
        public AppState()
        {
            ScopeDefaults[CONVERSATION_SCOPE] = new ConversationState();
            ScopeDefaults[USER_SCOPE] = new WorkflowUserState();
        }

        /// <summary>
        /// Stores all the conversation-related state.
        /// </summary>
        public new ConversationState Conversation
        {
            get
            {
                TurnStateEntry? scope = GetScope(CONVERSATION_SCOPE);

                if (scope == null)
                {
                    throw new ArgumentException("TurnState hasn't been loaded. Call LoadStateAsync() first.");
                }

                return (ConversationState)scope.Value!;
            }
            set
            {
                TurnStateEntry? scope = GetScope(CONVERSATION_SCOPE);

                if (scope == null)
                {
                    throw new ArgumentException("TurnState hasn't been loaded. Call LoadStateAsync() first.");
                }

                scope.Replace(value!);
            }
        }

        /// <summary>
        /// Stores all the user-related workflow state across conversations.
        /// </summary>
        public new WorkflowUserState User
        {
            get
            {
                TurnStateEntry? scope = GetScope(USER_SCOPE);

                if (scope == null)
                {
                    throw new ArgumentException("TurnState hasn't been loaded. Call LoadStateAsync() first.");
                }

                return (WorkflowUserState)scope.Value!;
            }
            set
            {
                TurnStateEntry? scope = GetScope(USER_SCOPE);

                if (scope == null)
                {
                    throw new ArgumentException("TurnState hasn't been loaded. Call LoadStateAsync() first.");
                }

                scope.Replace(value!);
            }
        }
    }

    public class MyTask
    {
        public string Title { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// User state that persists across conversations - contains workflow state and user data
    /// </summary>
    public class WorkflowUserState : Record
    {
        private WorkflowStep? _cachedCurrentStep = null;
        private bool _currentStepCacheValid = false;

        // TEMPORARY: Store CurrentStep as both enum and string to debug serialization issues
        // Teams AI Framework may have issues serializing enums properly to Azure Blob Storage
        public WorkflowStep CurrentStep
        {
            get
            {
                // Return cached value if still valid
                if (_currentStepCacheValid && _cachedCurrentStep.HasValue)
                {
                    Console.WriteLine($"🎯 CurrentStep returning cached value: {_cachedCurrentStep.Value}");
                    return _cachedCurrentStep.Value;
                }

                try
                {
                    // TEMP FIX: Store as string to avoid enum serialization issues
                    var storedString = Get<string>("currentStepString");
                    if (!string.IsNullOrEmpty(storedString))
                    {
                        Console.WriteLine($"🔍 CurrentStep found string value: '{storedString}'");
                        if (Enum.TryParse<WorkflowStep>(storedString, out var parsedStep))
                        {
                            Console.WriteLine($"✅ CurrentStep parsed from string '{storedString}' to {parsedStep}");
                            _cachedCurrentStep = parsedStep;
                            _currentStepCacheValid = true;
                            return parsedStep;
                        }
                        else
                        {
                            Console.WriteLine($"❌ CurrentStep failed to parse string '{storedString}'");
                        }
                    }
                    else
                    {
                        Console.WriteLine("🔍 CurrentStep string value not found");
                    }

                    // Fallback to original logic for backward compatibility
                    if (TryGetValue<object>("currentStep", out var rawValue))
                    {
                        Console.WriteLine($"🔍 CurrentStep raw value: {rawValue} (Type: {rawValue?.GetType()})");
                        Console.WriteLine($"🔍 CurrentStep raw value as string: '{rawValue?.ToString()}'");
                        
                        WorkflowStep result;
                        
                        // Handle different value types stored in state
                        switch (rawValue)
                        {
                            case WorkflowStep step:
                                Console.WriteLine($"✅ CurrentStep as WorkflowStep: {step} (int value: {(int)step})");
                                result = step;
                                break;
                                
                            case int stepInt:
                                result = (WorkflowStep)stepInt;
                                Console.WriteLine($"✅ CurrentStep converted from int {stepInt} to {result}");
                                break;
                                
                            case string stepString when Enum.TryParse<WorkflowStep>(stepString, out var parsedStep2):
                                result = parsedStep2;
                                Console.WriteLine($"✅ CurrentStep parsed from string '{stepString}' to {result}");
                                break;
                                
                            case string stepString when int.TryParse(stepString, out var stepStringInt):
                                result = (WorkflowStep)stepStringInt;
                                Console.WriteLine($"✅ CurrentStep parsed from string '{stepString}' as int {stepStringInt} to {result}");
                                break;
                                
                            default:
                                Console.WriteLine($"❌ CurrentStep unknown type: {rawValue?.GetType()}, value: {rawValue}");
                                result = WorkflowStep.PROJECT_TO_BE_CREATED;
                                break;
                        }
                        
                        // Cache the result
                        _cachedCurrentStep = result;
                        _currentStepCacheValid = true;
                        return result;
                    }
                    else
                    {
                        Console.WriteLine("🔍 CurrentStep key 'currentStep' not found in state, using default");
                        Console.WriteLine($"🔍 All keys in state: {string.Join(", ", this.GetType().GetProperties().Select(p => p.Name))}");
                        
                        var defaultResult = WorkflowStep.PROJECT_TO_BE_CREATED;
                        _cachedCurrentStep = defaultResult;
                        _currentStepCacheValid = true;
                        return defaultResult;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ CurrentStep getter exception: {ex.Message}");
                    Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                    // Return default value if any casting fails
                    var errorResult = WorkflowStep.PROJECT_TO_BE_CREATED;
                    _cachedCurrentStep = errorResult;
                    _currentStepCacheValid = true;
                    return errorResult;
                }
            }
            set 
            { 
                Console.WriteLine($"🔄 Setting CurrentStep to: {value} (int value: {(int)value})");
                Console.WriteLine($"🔄 About to call Set('currentStep', {value})");
                
                // TEMP FIX: Store as both string and enum to debug serialization
                Set("currentStep", value);
                Set("currentStepString", value.ToString());
                
                // Update cache
                _cachedCurrentStep = value;
                _currentStepCacheValid = true;
                Console.WriteLine($"🔄 CurrentStep set complete - stored value should be: {value}");
                
                // Immediately verify what was stored
                try
                {
                    var verification = Get<object>("currentStep");
                    var verificationString = Get<string>("currentStepString");
                    Console.WriteLine($"🔄 VERIFICATION: Stored enum value is: {verification} (Type: {verification?.GetType()})");
                    Console.WriteLine($"🔄 VERIFICATION: Stored string value is: '{verificationString}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"🔄 VERIFICATION FAILED: {ex.Message}");
                }
            }
        }

        public string EmailId
        {
            get => Get<string>("emailId") ?? string.Empty;
            set => Set("emailId", value);
        }

        public string StateId
        {
            get => Get<string>("stateId") ?? GenerateStateId();
            set => Set("stateId", value);
        }

        public string ProjectId
        {
            get => Get<string>("projectId") ?? string.Empty;
            set => Set("projectId", value);
        }

        public string EngagementId
        {
            get => Get<string>("engagementId") ?? string.Empty;
            set => Set("engagementId", value);
        }

        public string LastError
        {
            get => Get<string>("lastError") ?? string.Empty;
            set => Set("lastError", value);
        }

        public DateTime LastActivityTime
        {
            get => Get<DateTime>("lastActivityTime");
            set => Set("lastActivityTime", value);
        }

        /// <summary>
        /// Store essential workflow data as simple strings to avoid serialization issues
        /// </summary>
        public string ProjectTitle
        {
            get => Get<string>("projectTitle") ?? string.Empty;
            set => Set("projectTitle", value);
        }

        public string ProjectDescription
        {
            get => Get<string>("projectDescription") ?? string.Empty;
            set => Set("projectDescription", value);
        }

        public string MilestonesJson
        {
            get => Get<string>("milestonesJson") ?? string.Empty;
            set => Set("milestonesJson", value);
        }

        public string SuppliersJson
        {
            get => Get<string>("suppliersJson") ?? string.Empty;
            set => Set("suppliersJson", value);
        }

        public string LastApiResponse
        {
            get => Get<string>("lastApiResponse") ?? string.Empty;
            set => Set("lastApiResponse", value);
        }

        /// <summary>
        /// Generate state ID in YYYYMMDDHHMM format
        /// </summary>
        private string GenerateStateId()
        {
            return DateTime.UtcNow.ToString("yyyyMMddHHmm");
        }

        /// <summary>
        /// Invalidate the CurrentStep cache - call this when state is loaded from storage
        /// </summary>
        public void InvalidateCurrentStepCache()
        {
            Console.WriteLine($"🔄 InvalidateCurrentStepCache called - before: _currentStepCacheValid={_currentStepCacheValid}, _cachedCurrentStep={_cachedCurrentStep}");
            _currentStepCacheValid = false;
            _cachedCurrentStep = null;
            Console.WriteLine("🔄 CurrentStep cache invalidated - cache is now invalid");
        }
    }

    // This class adds custom properties to the turn state which will be accessible in the various handler methods.
    public class ConversationState : Record
    {
        public Dictionary<string, MyTask> Tasks
        {
            get => Get<Dictionary<string, MyTask>>("tasks") ?? new Dictionary<string, MyTask>();
            set => Set("tasks", value);
        }
    }
}
