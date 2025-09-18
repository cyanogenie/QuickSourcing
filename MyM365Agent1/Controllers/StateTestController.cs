using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using MyM365Agent1.Model;

namespace MyM365Agent1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StateTestController : ControllerBase
    {
        private readonly IStorage _storage;

        public StateTestController(IStorage storage)
        {
            _storage = storage;
        }

        [HttpGet("test-state")]
        public async Task<IActionResult> TestState()
        {
            var testKey = "test-user-state";
            
            try
            {
                Console.WriteLine("🧪 STATE TEST: Starting state persistence test");
                
                // Create test user state
                var testUserState = new WorkflowUserState();
                testUserState.ProjectId = "TEST-999";
                testUserState.EngagementId = "TEST-ENG-123";
                testUserState.CurrentStep = WorkflowStep.PROJECT_CREATED;
                
                Console.WriteLine($"🧪 STATE TEST: Created test state - ProjectId={testUserState.ProjectId}, EngagementId={testUserState.EngagementId}, CurrentStep={testUserState.CurrentStep} ({(int)testUserState.CurrentStep})");
                
                // Save state directly to storage
                var changes = new Dictionary<string, object>
                {
                    [testKey] = testUserState
                };
                
                Console.WriteLine("🧪 STATE TEST: About to save state to storage");
                await _storage.WriteAsync(changes);
                Console.WriteLine("🧪 STATE TEST: State saved successfully");
                
                // Read state back directly from storage
                Console.WriteLine("🧪 STATE TEST: About to read state from storage");
                var keys = new[] { testKey };
                var readResult = await _storage.ReadAsync(keys);
                
                if (readResult.TryGetValue(testKey, out var readStateObj) && readStateObj is WorkflowUserState readState)
                {
                    Console.WriteLine($"🧪 STATE TEST: Read state - ProjectId={readState.ProjectId}, EngagementId={readState.EngagementId}, CurrentStep={readState.CurrentStep} ({(int)readState.CurrentStep})");
                    
                    var isMatch = readState.ProjectId == testUserState.ProjectId &&
                                 readState.EngagementId == testUserState.EngagementId &&
                                 readState.CurrentStep == testUserState.CurrentStep;
                    
                    return Ok(new
                    {
                        success = true,
                        stateMatch = isMatch,
                        saved = new
                        {
                            ProjectId = testUserState.ProjectId,
                            EngagementId = testUserState.EngagementId,
                            CurrentStep = testUserState.CurrentStep.ToString(),
                            CurrentStepInt = (int)testUserState.CurrentStep
                        },
                        loaded = new
                        {
                            ProjectId = readState.ProjectId,
                            EngagementId = readState.EngagementId,
                            CurrentStep = readState.CurrentStep.ToString(),
                            CurrentStepInt = (int)readState.CurrentStep
                        }
                    });
                }
                else
                {
                    Console.WriteLine("🧪 STATE TEST: Failed to read state back - no data found or wrong type");
                    Console.WriteLine($"🧪 STATE TEST: Read result type: {readStateObj?.GetType()}");
                    return BadRequest(new { error = "Failed to read state back", readType = readStateObj?.GetType()?.Name });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🧪 STATE TEST: Exception - {ex.Message}");
                return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }
        
        [HttpGet("check-blob")]
        public async Task<IActionResult> CheckBlob()
        {
            try
            {
                var keys = new[] { "test-user-state" };
                var result = await _storage.ReadAsync(keys);
                
                if (result.TryGetValue("test-user-state", out var stateObj) && stateObj is WorkflowUserState userState)
                {
                    return Ok(new
                    {
                        stateLoaded = true,
                        projectId = userState.ProjectId,
                        engagementId = userState.EngagementId,
                        currentStep = userState.CurrentStep.ToString(),
                        currentStepInt = (int)userState.CurrentStep,
                        storageType = _storage.GetType().Name
                    });
                }
                else
                {
                    return Ok(new
                    {
                        stateLoaded = false,
                        foundKeys = result.Keys.ToArray(),
                        hasData = result.Any(),
                        dataType = stateObj?.GetType()?.Name,
                        storageType = _storage.GetType().Name
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }
    }
}