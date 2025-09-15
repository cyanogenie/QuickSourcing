# Testing GraphQL Functionality

## Quick Test Examples

Once your M365 agent is running, you can test the GraphQL functionality with these example requests:

### Test 1: Get Event Details
**User Message**: "Get event details for event ID 14781"

**Expected Behavior**: 
- The agent will execute the predefined GraphQL query
- Return comprehensive event information including supplier details, contacts, and status

### Test 2: Custom GraphQL Query
**User Message**: "Execute this GraphQL query with event ID 14781: query($eventId: Int!) { eventDetails(eventId: $eventId) { eventId eventTitle supplierName eventStatus } }"

**Expected Behavior**:
- Execute the custom query with the specified variables
- Return only the requested fields (eventId, eventTitle, supplierName, eventStatus)

### Test 3: Error Handling
**User Message**: "Get event details for event ID abc"

**Expected Behavior**:
- Return an error message indicating that a valid numeric event ID is required

## Configuration Check

Before testing, ensure your `appsettings.Development.json` has:
```json
{
  "ExternalApi": {
    "BaseUrl": "https://afd-src-eventsmanagement-uat-001.azurefd.net/graphql?tenant=ProcureWeb?action=getEventDetails",
    "BearerToken": "Bearer [your-jwt-token]"
  }
}
```

## Debugging Tips

1. **Check Logs**: Look for GraphQL request logs to see what's being sent
2. **Verify Token**: Ensure your bearer token hasn't expired
3. **Test Endpoint**: Verify the GraphQL endpoint is accessible
4. **Check Response**: Look for GraphQL-specific errors in the response

## GraphQL Request Format

The service sends requests in this format:
```json
{
  "query": "query($eventId: Int!) { eventDetails(eventId: $eventId) { ... } }",
  "variables": {
    "eventId": 14781
  }
}
```

With headers:
```
Authorization: Bearer [token]
Content-Type: application/json
Accept: application/json
```
