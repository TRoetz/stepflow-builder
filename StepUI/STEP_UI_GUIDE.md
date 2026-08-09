# Step Functions UI System Guide

## Overview

This guide provides comprehensive documentation for the Step Functions UI system, which enables visual step flow building, modal-based step configuration, API registry management, and easy maintenance of step functions to replace SSIS packages.

## Architecture

The Step UI system consists of:

### 1. Visual Step Builder Canvas
- Drag-and-drop step node placement
- Connection-based flow building
- Real-time step reordering
- Step template library

### 2. Step Configuration Modals
- Dedicated modal for each step type
- Tabbed configuration interface (Basic/Advanced/Rules)
- Real-time validation
- Preview mode

### 3. API Registry Management
- API registration and maintenance
- API categorization
- Usage tracking
- Connection testing

### 4. Rule Editor
- Visual rule builder
- Condition testing
- Outcome configuration

## Step Types

### AI/LLM Steps
- Model configuration
- Prompt engineering
- Temperature and token control
- Function call support
- Output schema definition

### Rule Engine Steps
- Rule conditions
- Default outcomes
- Test mode
- Microsoft Rules Engine integration

### SQL Lookup Steps
- Database connection
- Query building
- Parameter injection
- Result mapping
- Caching support

### API Call Steps
- HTTP method selection
- Path configuration
- Header management
- Payload construction
- Response mapping
- Registered API integration

### Pass Steps
- Simple pass-through
- Data passing

### Script Execution Steps
- Language selection
- Script content
- Environment variables
- Timeout control

### DuckDB Steps
- Query building
- Result transformation
- Caching

### HTTP Request Steps
- REST API integration
- Request/response handling

### JSONAT Steps
- Expression-based transformation
- Path manipulation

### EAV Steps
- Entity-Attribute-Value operations
- CRUD operations

## API Registry

### Registering a New API
```typescript
const api = {
  apiId: "payment_api_001",
  name: "Payment Gateway",
  description: "Process payments",
  endpoint: "https://api.payment.example.com/charge",
  method: "POST",
  path: "/charge",
  headers: {
    "Content-Type": "application/json"
  },
  authentication: {
    type: "bearer",
    tokenSource: "azureKeyVault"
  },
  parameters: {
    amount: "number",
    currency: "string"
  },
  timeout: 30,
  retries: 3,
  tags: ["payment", "transaction"]
};
```

### Using Registered API in Step
```typescript
const step = {
  stepType: "API",
  configuration: {
    useRegisteredApi: true,
    registeredApiId: "payment_api_001",
    parameters: {
      amount: "$.orderData.amount"
    }
  }
};
```

### Creating New API Call (Not Registered)
```typescript
const step = {
  stepType: "API",
  configuration: {
    useRegisteredApi: false,
    method: "POST",
    path: "/api/new-endpoint",
    headers: {
      "Authorization": "Bearer {token}"
    },
    parameters: {
      id: "$.orderData.id"
    }
  }
};
```

## Step Configuration Workflow

### 1. Adding a New Step
1. Click on canvas to place step
2. Select step type from template
3. Drag step to desired position
4. Click "Connect" to link to next step

### 2. Configuring a Step
1. Click step to open configuration modal
2. Navigate tabs:
   - **Basic**: Core step properties
   - **Advanced**: Detailed settings
   - **Rules**: Rule engine configuration (for RULE steps)
3. Fill in required fields
4. Click "Validate" to check configuration
5. Click "Save Step" to apply

### 3. Managing Connections
- Click "Connect" on step to create outgoing connection
- Use conditional connections for gateways
- Use error connections for exception handling

### 4. Using Registered APIs
1. Open API step configuration
2. Enable "Use Registered API"
3. Select from available APIs dropdown
4. Configure parameters using JSONPath

### 5. Creating New APIs
1. Go to API Registry tab
2. Click "Register New API"
3. Fill in API details
4. Click "Save API"
5. API becomes available for use in steps

## Rule Configuration

### Adding a Rule
1. Open RULE step configuration
2. Switch to "Rules" tab
3. Click "+ Add Rule"
4. Configure:
   - Rule name
   - Conditions (field, operator, value)
   - Default outcome (pass/fail/exception)

### Condition Types
- **Numeric**: >, <, >=, <=, ==, !=
- **String**: ==, !=, contains, startsWith, endsWith, matches
- **Boolean**: ==, !=
- **Date**: >, <, >=, <=

### Testing Rules
1. Enable "Test Mode"
2. Add test cases
3. Run tests to validate rule logic

## Visual Builder Features

### Drag and Drop
- Drag steps between columns
- Reorder steps within column
- Drag from template to canvas

### Connection Types
- **Normal**: Sequential flow
- **Conditional**: Gateway branches
- **Error**: Exception handling

### Step Templates
- AI/LLM
- Rule Engine
- SQL Lookup
- API Call
- Pass Step
- Script Execution
- DuckDB Query
- HTTP Request

## Migration from SSIS

### Data Flow Tasks
- Convert to SQL or API steps
- Use SQL step for database queries
- Use API step for external service calls

### Script Tasks
- Convert to Script Execution step
- Maintain script content
- Configure timeout and environment

### Sequence Tasks
- Convert to AI or Rule steps
- Use appropriate step type for logic

### Parallel Execution
- Use parallel columns in visual builder
- Configure parallel processing in step configuration

## Maintenance

### Adding New Step Types
1. Update `StepConfigFactory` in `stepConfig.ts`
2. Add new step type constant
3. Create modal component in `StepTypes/`
4. Add to step templates in canvas

### Updating API Registry
1. Modify `eav_registry.json` or API definitions
2. Update API categorization
3. Update authentication methods

### Testing Step Functions
1. Enable test mode in step configuration
2. Add test cases
3. Run validation
4. Review execution logs

## Best Practices

### Step Naming
- Use descriptive names
- Follow naming conventions
- Include context in names

### Configuration
- Use JSONPath for parameters
- Validate all configurations
- Use appropriate timeouts

### API Management
- Register APIs when possible
- Document API usage
- Track API dependencies

### Rule Management
- Use test mode for new rules
- Document rule logic
- Version rule changes

## Troubleshooting

### Step Validation Fails
- Check all required fields
- Review validation errors
- Fix configuration issues

### Connection Issues
- Verify API endpoints
- Check authentication
- Review timeout settings

### Rule Execution Errors
- Enable test mode
- Review rule conditions
- Check outcome configuration

## Next Steps

1. **Explore Modal Components**: Review each step type modal
2. **Configure API Registry**: Set up your APIs
3. **Build Sample Flows**: Create example step functions
4. **Test and Validate**: Run validation on all steps
5. **Document**: Document your step configurations

## Support

For issues or questions:
- Review code comments
- Check TypeScript types
- Examine existing examples
- Contact development team
