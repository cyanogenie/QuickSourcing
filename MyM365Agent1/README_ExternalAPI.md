# External API Integration

This M365 agent now supports making HTTP POST requests to external APIs with bearer token authentication, including specialized GraphQL query support.

## Configuration

Update your `appsettings.json` or `appsettings.Development.json` with your external API settings:

```json
{
  "ExternalApi": {
    "BaseUrl": "https://your-api.example.com",
    "BearerToken": "your-bearer-token-here"
  }
}
```

**Note**: For GraphQL endpoints, the BaseUrl should point to your GraphQL endpoint (e.g., `https://api.example.com/graphql`).

## Usage

The agent now supports multiple types of API calls:

### 1. GraphQL Event Details Query

Get detailed information about an event using its ID:

**Example User Requests:**
- "Get event details for event ID 14781"
- "Show me the details of event 12345"
- "Retrieve information for event 14781"

This will execute the predefined GraphQL query to fetch comprehensive event details including:
- Event ID and basic info
- Supplier information
- Contact details
- Event status and processing info
- Approver and owner information

### 2. Custom GraphQL Queries

Execute any GraphQL query:

**Example User Requests:**
- "Execute this GraphQL query: { users { id name email } }"
- "Run a GraphQL query to get all events with status 'active'"

### 3. Regular HTTP POST Requests

Make standard REST API calls:

**Example User Requests:**
- "Make an API call to the /users endpoint with data {name: 'John', email: 'john@example.com'}"
- "Post to /orders endpoint with order data"

## Available Actions

### 1. `getEventDetails`
- **Purpose**: Retrieve comprehensive event information by event ID
- **Parameters**: 
  - `eventId` (required): Numeric event ID
- **Query Structure**: Uses the predefined GraphQL query for event details

### 2. `executeGraphQLQuery`
- **Purpose**: Execute custom GraphQL queries
- **Parameters**:
  - `query` (required): GraphQL query string
  - `variables` (optional): Query variables as JSON object

### 3. `postToExternalApi`
- **Purpose**: Make standard HTTP POST requests
- **Parameters**:
  - `endpoint` (required): API endpoint path
  - `data` (required): JSON data to send
  - `headers` (optional): Additional HTTP headers

## GraphQL Query Template

The predefined event details query includes:

```graphql
query($eventId: Int!) {
  eventDetails(eventId: $eventId) {
    eventId
    microsoftSupplierId
    companyCode
    country
    currency
    programName
    eventTitle
    projectType
    eventStatus
    lastAction
    isSystemProcfessing
    systemProcessingAction
    supplierName
    supplierLegalName
    supplierUvpName
    inCountrySupplierContact {
      firstName
      lastName
      email
    }
    supplierType
    supplierCcContactEmail
    supplierAdminContacts
    owner
    eventRequestor
    msContactEmail
    msCcContactEmail
    approver
  }
}
```

## Security Notes

- The bearer token is securely stored in configuration
- All API calls are logged for debugging
- Error handling is implemented to provide user-friendly feedback
- The service validates configuration before making requests
- GraphQL queries are parameterized to prevent injection attacks

## Error Handling

The service handles various error scenarios:
- Missing configuration (base URL or bearer token)
- Network timeouts
- HTTP error responses
- Invalid JSON data
- GraphQL query errors
- Service unavailability

All errors are logged and user-friendly messages are returned to the chat.
