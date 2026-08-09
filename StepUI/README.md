# Step Functions UI System

## Overview

The Step Functions UI system provides a comprehensive visual interface for building, configuring, and managing step-based workflows. This system replaces SSIS packages with a modern, maintainable, and testable step function architecture.

## Key Features

### 1. Visual Step Builder
- Drag-and-drop step placement on canvas
- Connection-based flow building
- Real-time step reordering
- Template library for common step types

### 2. Modal-Based Step Configuration
- Dedicated modal for each step type (AI, RULE, SQL, API, etc.)
- Tabbed configuration (Basic/Advanced/Rules)
- Real-time preview
- Validation support

### 3. API Registry Management
- Register and maintain APIs
- API categorization
- Usage tracking
- Connection testing

### 4. Rule Configuration
- Visual rule builder
- Condition testing
- Outcome configuration
- Test mode for validation

### 5. SSIS Package Replacement
- Data Flow Tasks → SQL/API steps
- Script Tasks → Script Execution steps
- Sequence Tasks → AI/Rule steps
- Parallel execution support

## Step Types

| Type | Icon | Description | Primary Use |
|------|------|-------------|-------------|
| AI/LLM | 🤖 | AI/LLM decision making | AI-driven decisions |
| RULE | ⚖️ | Rule engine execution | Business rules |
| SQL | 📊 | SQL database queries | Database lookups |
| API | 🔌 | API calls | External service calls |
| PASS | ⏭️ | Pass-through step | Data passing |
| SCRIPT | 💻 | Script execution | Custom logic |
| HTTP | 🌐 | HTTP requests | REST API calls |
| DUCKDB | 🦆 | DuckDB queries | Analytics queries |
| JSONAT | 📝 | JSON transformation | Data transformation |
| EAV | 📦 | EAV operations | Attribute management |

## Quick Start

### 1. Add a New Step
1. Click on the canvas to place a step
2. Select step type from template
3. Drag step to desired position
4. Click "Connect" to link to next step

### 2. Configure a Step
1. Click step to open configuration modal
2. Navigate tabs:
   - **Basic**: Core step properties
   - **Advanced**: Detailed settings
   - **Rules**: Rule engine configuration
3. Fill in required fields
4. Click "Validate" to check configuration
5. Click "Save Step" to apply

### 3. Use Registered API
1. Open API step configuration
2. Enable "Use Registered API"
3. Select from available APIs dropdown
4. Configure parameters using JSONPath

### 4. Create New API
1. Go to API Registry tab
2. Click "Register New API"
3. Fill in API details
4. Click "Save API"
5. API becomes available for use in steps

## Architecture

```
StepUI/
├── Components/
│   ├── StepConfig/
│   │   ├── StepConfigModal.tsx          # Main step configuration modal
│   │   └── StepConfigForm.tsx           # Step form component
│   ├── StepTypes/
│   │   ├── AIStepModal.tsx               # AI/LLM step modal
│   │   ├── RuleStepModal.tsx             # Rule step modal
│   │   ├── SQLStepModal.tsx              # SQL lookup step modal
│   │   ├── APIStepModal.tsx              # API call step modal
│   │   ├── PassStepModal.tsx             # Pass step modal
│   │   ├── ScriptStepModal.tsx           # Script execution step modal
│   │   ├── HTTPStepModal.tsx             # HTTP request step modal
│   │   ├── DuckDBStepModal.tsx           # DuckDB query step modal
│   │   ├── JSONATStepModal.tsx           # JSONAT processor step modal
│   │   └── EAVStepModal.tsx              # EAV operation step modal
│   ├── APIManager/
│   │   ├── APIRegistry.tsx               # API registry UI
│   │   ├── APIForm.tsx                   # API creation form
│   │   └── APIManagementService.ts       # API management logic
│   ├── StepBuilder/
│   │   ├── StepBuilderCanvas.tsx         # Visual step builder canvas
│   │   ├── StepNode.tsx                  # Individual step node
│   │   ├── StepConnections.tsx           # Connection builder
│   │   └── StepBuilderService.ts         # Builder logic
│   └── RuleEditor/
│       ├── RuleEditor.tsx                # Rule configuration editor
│       ├── RuleBuilder.tsx               # Rule builder UI
│       └── RuleValidator.tsx             # Rule validation
├── Services/
│   ├── StepConfigService.ts              # Step configuration logic
│   ├── StepExecutionService.ts           # Step execution handling
│   └── APIRegistryService.ts             # API registry management
├── Types/
│   ├── stepConfig.ts                     # Step configuration types
│   ├── apiRegistry.ts                    # API registry types
│   └── stepTypes.ts                      # Step type definitions
├── Utils/
│   ├── stepValidator.ts                  # Step validation utilities
│   └── ruleValidator.ts                  # Rule validation utilities
└── Constants/
    ├── stepTypeConstants.ts              # Step type constants
    └── apiRegistryConstants.ts           # API registry constants
```

## Usage Examples

### Creating a Step Configuration
```typescript
const step = {
  stepId: "api_1",
  stepType: "API",
  title: "Payment Processing",
  description: "Process payment for order",
  resource: "https://api.payment.example.com/charge",
  next: "sendConfirmation",
  configuration: {
    method: "POST",
    path: "/charge",
    headers: {
      "Content-Type": "application/json",
      "Authorization": "Bearer {token}"
    },
    parameters: {
      amount: "$.orderData.amount",
      currency: "$.orderData.currency",
      customerId: "$.orderData.customerId"
    },
    requestPayload: {
      amount: "$.amount",
      currency: "$.currency"
    },
    responseMapping: {
      transactionId: "$.transactionId",
      status: "$.status"
    },
    timeout: 30,
    retries: 3,
    errorHandling: "continue",
    authenticated: true,
    useRegisteredApi: false
  },
  metadata: {
    createdAt: new Date().toISOString(),
    createdBy: "user",
    lastModified: new Date().toISOString(),
    version: 1
  }
};
```

### Using Registered API
```typescript
const step = {
  stepId: "api_2",
  stepType: "API",
  title: "Payment Gateway",
  configuration: {
    useRegisteredApi: true,
    registeredApiId: "payment_api_001",
    parameters: {
      amount: "$.orderData.amount",
      customerId: "$.orderData.customerId"
    }
  }
};
```

### Creating Rule Step
```typescript
const step = {
  stepId: "rule_1",
  stepType: "RULE",
  title: "Credit Validation",
  resource: "rule://CreditValidation",
  configuration: {
    engine: "microsoftRulesEngine",
    rules: [
      {
        name: "MinCreditScore",
        conditions: [
          {
            field: "creditScore",
            operator: ">=",
            value: 600
          }
        ],
        description: "Check minimum credit score",
        outcome: "pass"
      }
    ],
    defaultOutcome: "pass",
    testMode: false
  }
};
```

### Creating SQL Step
```typescript
const step = {
  stepId: "sql_1",
  stepType: "SQL",
  title: "Customer Lookup",
  resource: "duckdb://lookup",
  configuration: {
    database: "customers.db",
    query: "SELECT * FROM customers WHERE customer_id = {customer_id}",
    parameters: {
      customer_id: "$.orderData.customerId"
    },
    columnsToReturn: ["customer_name", "credit_score", "status"],
    databaseType: "duckdb",
    connectionTimeout: 30,
    useCaching: true,
    cacheTTL: 60
  }
};
```

### Creating AI Step
```typescript
const step = {
  stepId: "ai_1",
  stepType: "AI",
  title: "AI Credit Review",
  resource: "ai://decision",
  configuration: {
    model: "gpt-4-turbo",
    prompt: "Analyze the customer's order and provide a credit decision based on their payment history",
    systemPrompt: "You are a credit decision AI assistant. Review payment history and provide approval or rejection with reasoning.",
    temperature: 0.7,
    maxTokens: 200,
    promptVariables: {
      orderAmount: "$.orderData.amount",
      customerName: "$.orderData.customerName"
    },
    llmService: "azureOpenAI",
    outputFormat: "json",
    outputSchema: {
      decision: "approve|reject",
      reason: "string",
      confidence: "number"
    }
  }
};
```

## API Registry

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

## Documentation

- **STEP_UI_GUIDE.md**: Detailed user guide for all features
- **ARCHITECTURE_SUMMARY.md**: Complete architecture overview
- **README.md**: This overview file

## Maintenance

### Adding New Step Types
1. Update `StepConfigFactory` in `Types/stepConfig.ts`
2. Create modal component in `Components/StepTypes/`
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

## Summary

This Step Functions UI system provides:
1. **Visual Step Builder**: Drag-and-drop flow creation
2. **Modal Configuration**: Dedicated interface per step type
3. **API Management**: Registry and usage tracking
4. **Rule Configuration**: Visual rule building and testing
5. **SSIS Replacement**: Modern, maintainable step functions
6. **Easy Maintenance**: Structured step types and templates

This architecture enables easy step creation, API management, and rule configuration while providing a modern alternative to SSIS packages.
