using Microsoft.Bot.Builder;
using Microsoft.Teams.AI.State;
using MyM365Agent1.Model;

namespace MyM365Agent1.Services
{
    /// <summary>
    /// Custom blob storage wrapper that generates readable blob names using engagement ID
    /// </summary>
    public class CustomBlobStorage : IStorage
    {
        private readonly IStorage _innerStorage;
        private readonly ILogger<CustomBlobStorage> _logger;
        private readonly Dictionary<string, string> _keyMappings = new();

        public CustomBlobStorage(IStorage innerStorage, ILogger<CustomBlobStorage> logger)
        {
            _innerStorage = innerStorage;
            _logger = logger;
        }

        public async Task<IDictionary<string, object>> ReadAsync(string[] keys, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("🔍 CustomBlobStorage.ReadAsync - Original keys: {Keys}", string.Join(", ", keys));
            
            // Try to read from both original keys AND any mapped readable keys
            var keysToRead = new HashSet<string>(keys);
            
            // Add potential readable keys for user state
            foreach (var key in keys)
            {
                if (key.Contains("/users/"))
                {
                    // Try to find existing readable keys that might match this user
                    // For now, we'll try a pattern-based approach
                    var potentialReadableKeys = await TryFindReadableKeysForUser(key);
                    foreach (var readableKey in potentialReadableKeys)
                    {
                        keysToRead.Add(readableKey);
                    }
                }
            }
            
            _logger.LogInformation("🔍 CustomBlobStorage.ReadAsync - All keys to try: {AllKeys}", string.Join(", ", keysToRead));
            
            var result = await _innerStorage.ReadAsync(keysToRead.ToArray(), cancellationToken);
            
            // Map back to original keys if we found data in readable keys
            var finalResult = new Dictionary<string, object>();
            foreach (var originalKey in keys)
            {
                if (result.TryGetValue(originalKey, out var value))
                {
                    // Found with original key
                    finalResult[originalKey] = value;
                    _logger.LogInformation("✅ Found data for original key: {OriginalKey}", originalKey);
                }
                else
                {
                    // Try to find in readable keys
                    var foundInReadableKey = false;
                    foreach (var kvp in result)
                    {
                        if (kvp.Key.StartsWith("engagement-") && originalKey.Contains("/users/"))
                        {
                            finalResult[originalKey] = kvp.Value;
                            foundInReadableKey = true;
                            _logger.LogInformation("✅ Found data for original key {OriginalKey} in readable key: {ReadableKey}", originalKey, kvp.Key);
                            break;
                        }
                    }
                    
                    if (!foundInReadableKey)
                    {
                        _logger.LogInformation("ℹ️ No data found for key: {OriginalKey}", originalKey);
                    }
                }
            }
            
            return finalResult;
        }

        public async Task WriteAsync(IDictionary<string, object> changes, CancellationToken cancellationToken = default)
        {
            var modifiedChanges = new Dictionary<string, object>();

            foreach (var kvp in changes)
            {
                var originalKey = kvp.Key;
                var value = kvp.Value;
                
                // Try to extract engagement ID from the state data for readable blob naming
                var readableKey = GenerateReadableKey(originalKey, value);
                
                _logger.LogInformation("🔄 CustomBlobStorage.WriteAsync - Original key: {OriginalKey}, Readable key: {ReadableKey}", 
                    originalKey, readableKey);
                
                // Store mapping for future reads
                if (readableKey != originalKey)
                {
                    _keyMappings[originalKey] = readableKey;
                }
                
                modifiedChanges[readableKey] = value;
            }

            await _innerStorage.WriteAsync(modifiedChanges, cancellationToken);
        }

        public async Task DeleteAsync(string[] keys, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("🗑️ CustomBlobStorage.DeleteAsync - Keys: {Keys}", string.Join(", ", keys));
            
            // Delete both original and readable keys
            var keysToDelete = new HashSet<string>(keys);
            foreach (var key in keys)
            {
                if (_keyMappings.TryGetValue(key, out var readableKey))
                {
                    keysToDelete.Add(readableKey);
                }
            }
            
            await _innerStorage.DeleteAsync(keysToDelete.ToArray(), cancellationToken);
        }

        /// <summary>
        /// Try to find readable keys that might correspond to a user key
        /// </summary>
        private async Task<IEnumerable<string>> TryFindReadableKeysForUser(string userKey)
        {
            try
            {
                // This is a simplified approach - in a real implementation you might want to
                // list blobs with a prefix pattern like "engagement-*"
                // For now, we'll rely on the cached mappings
                var readableKeys = new List<string>();
                
                if (_keyMappings.TryGetValue(userKey, out var mappedKey))
                {
                    readableKeys.Add(mappedKey);
                }
                
                // Also try to find all engagement-* keys (this is a simplified approach)
                // In production, you might want to implement blob listing functionality
                
                return readableKeys;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error finding readable keys for {UserKey}", userKey);
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Generate a readable blob key using engagement ID if available
        /// </summary>
        private string GenerateReadableKey(string originalKey, object value)
        {
            try
            {
                // Check if this is a user state key
                if (originalKey.Contains("/users/"))
                {
                    _logger.LogInformation("🔍 Analyzing user state key: {OriginalKey}", originalKey);
                    
                    // Check if this is a user state object with engagement ID
                    if (value is IDictionary<string, object> stateData)
                    {
                        var engagementId = ExtractValueFromState(stateData, "engagementId");
                        var emailId = ExtractValueFromState(stateData, "emailId");
                        
                        _logger.LogInformation("🔍 Extracted from user state - EngagementId: {EngagementId}, EmailId: {EmailId}", 
                            engagementId ?? "null", emailId ?? "null");
                        
                        if (!string.IsNullOrEmpty(engagementId))
                        {
                            // Create readable key: engagement-{engagementId}-{emailPrefix}
                            var emailPrefix = !string.IsNullOrEmpty(emailId) ? emailId.Split('@')[0] : "user";
                            var readableKey = $"engagement-{engagementId}-{emailPrefix}";
                            _logger.LogInformation("✅ Generated readable key: {ReadableKey} from engagement: {EngagementId}, email: {EmailId}", 
                                readableKey, engagementId, emailId ?? "null");
                            return readableKey;
                        }
                    }
                }
                
                // For conversation state, just use original key
                if (originalKey.Contains("/conversations/"))
                {
                    _logger.LogInformation("ℹ️ Using original conversation key: {OriginalKey}", originalKey);
                    return originalKey;
                }
                
                // Fallback to original key if we can't extract engagement ID
                _logger.LogInformation("ℹ️ Using original key: {OriginalKey} (no engagement ID found)", originalKey);
                return originalKey;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error generating readable key, using original: {OriginalKey}", originalKey);
                return originalKey;
            }
        }

        /// <summary>
        /// Extract a value from nested state data structure
        /// </summary>
        private string ExtractValueFromState(IDictionary<string, object> stateData, string key)
        {
            try
            {
                // Direct lookup
                if (stateData.TryGetValue(key, out var directValue))
                {
                    return directValue?.ToString() ?? string.Empty;
                }
                
                // Look through all nested objects for the key
                foreach (var kvp in stateData)
                {
                    if (kvp.Value is IDictionary<string, object> nestedDict)
                    {
                        if (nestedDict.TryGetValue(key, out var nestedValue))
                        {
                            return nestedValue?.ToString() ?? string.Empty;
                        }
                    }
                }
                
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error extracting {Key} from state", key);
                return string.Empty;
            }
        }
    }
}