# Step Functions UI System - Architecture Summary

## Executive Summary

The Step Functions UI system provides a comprehensive visual interface for building, configuring, and managing step-based workflows. This system replaces SSIS packages with a modern, maintainable, and testable step function architecture.

## Core Features

### 1. Visual Step Builder Canvas
- **Drag-and-Drop Interface**: Place steps anywhere on canvas
- **Connection-Based Flow**: Define workflow by connecting steps
- **Real-Time Validation**: Instant feedback on configuration
- **Template Library**: Quick access to common step types

### 2. Modal-Based Step Configuration
- **Dedicated Modal per Step Type**: Each step type has its own interface
- **Tabbed Configuration**: Basic/Advanced/Rules tabs for organization
- **Real-Time Preview**: See step configuration before saving
- **Validation Support**: Check configuration validity before saving

### 3. API Registry Management
- **API Registration**: Create and maintain registered APIs
- **API Categorization**: Organize APIs by category
- **Usage Tracking**: Monitor API usage and performance
- **Connection Testing**: Test API connectivity

### 4. Rule Configuration
- **Visual Rule Builder**: Create rules without coding
- **Condition Testing**: Test rule conditions
- **Outcome Configuration**: Define pass/fail/exception outcomes
- **Test Mode**: Validate rules before deployment

## Step Types and Configuration

### AI/LLM Steps
```typescript
{
  stepId: "ai_1",
  stepType: "AI",
  title: "AI Decision",
  configuration: {
    model: "gpt-4-turbo",
    prompt: "Analyze input...",
    temperature: 0.7,
    maxTokens: 200,
    llmService: "azureOpenAI",
    outputFormat: "json"
  }
}
```

### Rule Engine Steps
```typescript
{
  stepId: "rule_1",
  stepType: "RULE",
  title: "Credit Rules",
  configuration: {
    engine: "microsoftRulesEngine",
    rules: [
      {
        name: "MinCreditScore",
        conditions: [
          { field: "creditScore", operator: ">=", value: 600 }
        ],
        outcome: "pass"
      }
    ],
    defaultOutcome: "pass"
  }
}
```

### SQL Lookup Steps
```typescript
{
  stepId: "sql_1",
  stepType: "SQL",
  title: "Customer Lookup",
  configuration: {
    database: "customers.db",
    query: "SELECT * FROM customers WHERE id = {id}",
    parameters: { id: "$.orderId" },
    columnsToReturn: ["name", "email", "phone"],
    databaseType: "duckdb"
  }
}
```

### API Call Steps
```typescript
{
  stepId: "api_1",
  stepType: "API",
  title: "Payment Processing",
  configuration: {
    useRegisteredApi: true,
    registeredApiId: "payment_api_001",
    parameters: {
      amount: "$.orderData.amount",
      currency: "$.orderData.currency"
    },
    headers: { "Content-Type": "application/json" }
  }
}
```

### Pass Steps
```typescript
{
  stepId: "pass_1",
  stepType: "PASS",
  title: "Pass Through",
  configuration: {
    passData: { status: "pending" }
  }
}
```

### Script Execution Steps
```typescript
{
  stepId: "script_1",
  stepType: "SCRIPT",
  title: "Data Transformation",
  configuration: {
    language: "python",
    script: "def transform(data):\n    return data.upper()",
    parameters: { input: "$.data" },
    timeout: 30
  }
}
```

### DuckDB Steps
```typescript
{
  stepId: "duckdb_1",
  stepType: "DUCKDB",
  title: "Analytics Query",
  configuration: {
    database: "analytics.db",
    query: "SELECT date, SUM(amount) FROM transactions GROUP BY date",
    parameters: {},
    columnsToReturn: ["date", "total"],
    databaseType: "duckdb",
    useCaching: true,
    cacheTTL: 60
  }
}
```

### HTTP Request Steps
```typescript
{
  stepId: "http_1",
  stepType: "HTTP",
  title: "Webhook Send",
  configuration: {
    method: "POST",
    path: "https://example.com/webhook",
    headers: { "Authorization": "Bearer {token}" },
    parameters: { event: "$.eventType" },
    timeout: 30,
    retries: 3
  }
}
```

### JSONAT Steps
```typescript
{
  stepId: "jsonat_1",
  stepType: "JSONAT",
  title: "Data Transformation",
  configuration: {
    expression: "$.data.map(d => d.amount * 1.1)",
    inputPath: "$.data",
    outputPath: "$.transformed",
    resultPath: "$.result",
    errorHandling: "continue"
  }
}
```

### EAV Steps
```typescript
{
  stepId: "eav_1",
  stepType: "EAV",
  title: "Attribute Update",
  configuration: {
    entityType: "order",
    attribute: "status",
    operation: "update",
    keyValue: "completed",
    useEavRegistry: true,
    eavRegistryId: "orders_registry"
  }
}
```

## API Registry Structure

### Registered API Format
```typescript
{
  apiId: "payment_api_001",
  name: "Payment Gateway",
  description: "Process credit card payments",
  endpoint: "https://api.payment.example.com/charge",
  method: "POST",
  path: "/charge",
  headers: {
    "Content-Type": "application/json",
    "X-API-Version": "v1"
  },
  authentication: {
    type: "bearer",
    tokenSource: "azureKeyVault",
    tokenPath: "kv://payment-token"
  },
  parameters: {
    amount: "number",
    currency: "string",
    customerId: "string",
    transactionId: "string"
  },
  responseSchema: {
    type: "object",
    properties: {
      transactionId: { type: "string" },
      status: { type: "string" },
      amount: { type: "number" }
    }
  },
  timeout: 30,
  retries: 3,
  tags: ["payment", "transaction", "credit-card"],
  isActive: true,
  version: "1.0",
  category: "payment"
}
```

## Component Architecture

### Step Builder Canvas (`StepBuilderCanvas.tsx`)
- Main visual builder interface
- Step node management
- Connection drawing
- Drag-and-drop functionality

### Step Node (`StepNode.tsx`)
- Individual step representation
- Step type rendering
- Icon and color coding
- Connection buttons
- API selector display

### Step Connections (`StepConnections.tsx`)
- Connection line rendering
- Connection type styling
- Connection removal
- Arrow head markers

### Step Config Modal (`StepConfigModal.tsx`)
- Step configuration interface
- Tab navigation
- Validation display
- Save/Cancel actions

### Step Config Form (`StepConfigForm.tsx`)
- Step type-specific configuration
- Tabbed interface (Basic/Advanced/Rules)
- Real-time preview
- Validation support

### Step Types
- AI/LLM step modal
- Rule step modal
- SQL step modal
- API step modal
- Pass step modal
- Script step modal
- HTTP step modal
- DuckDB step modal
- JSONAT step modal
- EAV step modal

### API Manager
- API registry UI
- API registration form
- API search and filtering
- API usage tracking

### Rule Editor
- Visual rule builder
- Condition configuration
- Rule testing interface
- Outcome configuration

## File Structure

```
StepFunctionsApp/StepUI/
├── Components/
│   ├── StepConfig/
│   │   ├── StepConfigModal.tsx        # Main modal component
│   │   └── StepConfigForm.tsx         # Configuration form
│   ├── StepTypes/
│   │   ├── AIStepModal.tsx            # AI step configuration
│   │   ├── RuleStepModal.tsx          # Rule step configuration
│   │   ├── SQLStepModal.tsx           # SQL step configuration
│   │   ├── APIStepModal.tsx           # API step configuration
│   │   ├── PassStepModal.tsx          # Pass step configuration
│   │   ├── ScriptStepModal.tsx        # Script step configuration
│   │   ├── HTTPStepModal.tsx          # HTTP step configuration
│   │   ├── DuckDBStepModal.tsx        # DuckDB step configuration
│   │   ├── JSONATStepModal.tsx        # JSONAT step configuration
│   │   └── EAVStepModal.tsx           # EAV step configuration
│   ├── APIManager/
│   │   ├── APIRegistry.tsx            # API registry UI
│   │   ├── APIForm.tsx                # API registration form
│   │   └── APIManagementService.ts    # API management logic
│   ├── StepBuilder/
│   │   ├── StepBuilderCanvas.tsx      # Main canvas component
│   │   ├── StepNode.tsx               # Step node component
│   │   ├── StepConnections.tsx        # Connection rendering
│   │   └── StepBuilderService.ts      # Builder logic
│   └── RuleEditor/
│       ├── RuleEditor.tsx             # Rule configuration editor
│       ├── RuleBuilder.tsx            # Visual rule builder
│       └── RuleValidator.tsx          # Rule validation
├── Services/
│   ├── StepConfigService.ts           # Step configuration logic
│   ├── StepExecutionService.ts        # Step execution handling
│   └── APIRegistryService.ts          # API registry management
├── Types/
│   ├── stepConfig.ts                  # Step configuration types
│   ├── apiRegistry.ts                 # API registry types
│   └── stepTypes.ts                   # Step type definitions
├── Utils/
│   ├── stepValidator.ts               # Step validation utilities
│   └── ruleValidator.ts               # Rule validation utilities
├── Constants/
│   ├── stepTypeConstants.ts           # Step type constants
│   └── apiRegistryConstants.ts        # API registry constants
├── StepUI_GUIDE.md                    # User guide
├── ARCHITECTURE_SUMMARY.md            # This file
└── README.md                          # Overview
```

## Integration with StepFunctions App

### Flow Definition
```typescript
{
  name: "Order Processing Flow",
  description: "Process orders through validation, payment, and fulfillment",
  version: "1.0",
  startAt: "start_event",
  states: {
    "start_event": {
      type: "Task",
      resource: "pass://start",
      end: false,
      next: "validate_order"
    },
    "validate_order": {
      type: "Task",
      resource: "rule://validate",
      end: false,
      next: "check_credit"
    }
  }
}
```

### Step-to-SSIS Mapping
```typescript
{
  dataFlowTask: {
    stepType: "SQL" | "API";
    mapping: "query" | "endpoint";
  },
  scriptTask: {
    stepType: "SCRIPT";
    mapping: "script";
  },
  sequenceTask: {
    stepType: "AI" | "RULE";
    mapping: "decisionEngine";
  }
}
```

## Usage Examples

### Creating a New Flow
```typescript
// Open Step Builder Canvas
const canvas = renderStepBuilderCanvas({
  title: "New Flow",
  description: "Order Processing",
  apiRegistry: apiRegistryService.getRegisteredAPIs(),
  availableAPIs: apiRegistryService.searchAPIs(""),
  onStepAdded: (step) => stepConfigService.addStep(step),
  onStepRemoved: (stepId) => stepConfigService.removeStep(stepId),
  onConnectionCreated: (connection) => stepConfigService.addConnection(connection),
  onConnectionRemoved: (connectionId) => stepConfigService.removeConnection(connectionId),
  onSave: async (flow) => {
    await stepConfigService.saveFlow(flow);
  }
});
```

### Opening Step Configuration Modal
```typescript
// Open modal for API step
const modal = renderStepConfigModal({
  stepType: "API",
  existingStep: stepConfig,
  onSave: async (step) => {
    await stepConfigService.updateStep(step.stepId, step);
  },
  onCancel: () => {
    // Close modal
  },
  apiRegistry: apiRegistry,
  availableAPIs: availableAPIs
});
```

### Registering a New API
```typescript
// Open API registration form
const apiForm = renderAPIRegistryForm({
  onSave: async (api) => {
    await apiRegistryService.registerAPI(api);
  },
  onCancel: () => {
    // Close form
  }
});
```

## Maintenance Guidelines

### Adding New Step Types
1. Update `StepConfigFactory` in `stepConfig.ts`
2. Create modal component in `StepTypes/`
3. Add to step templates in `StepBuilderCanvas`
4. Update validation logic
5. Add to documentation

### Updating API Registry
1. Modify `eav_registry.json` or API definitions
2. Update API categorization
3. Update authentication methods
4. Test API connections
5. Deploy updated registry

### Migrating SSIS Packages
1. Identify SSIS package type (Data Flow/Script/Sequence)
2. Map to appropriate step type
3. Configure step using modal
4. Test step execution
5. Deploy to production

## Testing Strategy

### Unit Testing
- Test step configuration validation
- Test API registry operations
- Test rule configuration
- Test connection logic

### Integration Testing
- Test complete flow execution
- Test API call integration
- Test rule engine integration
- Test SQL execution

### User Acceptance Testing
- Verify visual builder usability
- Test step configuration workflow
- Validate API registration process
- Confirm SSIS migration success

## Summary

The Step Functions UI system provides:
1. **Visual Step Builder**: Drag-and-drop flow creation
2. **Modal Configuration**: Dedicated interface per step type
3. **API Management**: Registry and usage tracking
4. **Rule Configuration**: Visual rule building and testing
5. **SSIS Replacement**: Modern, maintainable step functions
6. **Easy Maintenance**: Structured step types and templates

This architecture enables easy step creation, API management, and rule configuration while providing a modern alternative to SSIS packages.
