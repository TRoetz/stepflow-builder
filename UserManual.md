# StepFlow Builder — User Manual

> A visual workflow designer and execution engine based on [Amazon States Language (ASL)](https://docs.aws.amazon.com/step-functions/home.html), with a node-based canvas for building, testing, and running state machines locally.

---

## Table of Contents

1. [Getting Started](#1-getting-started)
2. [The Canvas Interface](#2-the-canvas-interface)
3. [State Types](#3-state-types)
4. [Resource Schemes](#4-resource-schemes)
5. [Choice Rules](#5-choice-rules)
6. [Data Flow and Payload Handling](#6-data-flow-and-payload-handling)
7. [Error Handling](#7-error-handling)
8. [Node Categories and Schemas](#8-node-categories-and-schemas)
9. [Running Flows](#9-running-flows)
10. [Saving and Loading Flows](#10-saving-and-loading-flows)
11. [Keyboard Shortcuts](#11-keyboard-shortcuts)
12. [Importing Legacy Flows](#12-importing-legacy-flows)
13. [Examples](#13-examples)

---

## 1. Getting Started

### Prerequisites

| Requirement | Version |
|---|---|
| Node.js | 18+ (20 LTS recommended) |
| .NET SDK | 10 (for backend execution) |
| npm | 9+ |

### Installation

```bash
cd StepFunctionsApp
npm install
```

### Launch

| Command | Description |
|---|---|
| `npm run dev` | Start development server (port 5173) |
| `npm run build` | Production build |
| `npm run preview` | Preview production build |

---

## 2. The Canvas Interface

The StepFlow Builder provides a three-panel layout:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  TOOLBAR: [Flow Name]  [Run ▶] [Save] [Undo] [Redo] [Layout] [⚙️]           │
├──────────────┬──────────────────────────────────────┬───────────────────────┤
│  NODE        │  ┌─────────┐     ┌─────────┐        │  PROPERTY             │
│  PALETTE     │  │ AI      │────▶│ Rule    │        │  PANEL                │
│              │  │ Decision│     │ Engine  │        │  ┌───────────────┐    │
│  🔍 Search   │  └─────────┘     └─────────┘        │  │ Config        │    │
│              │  ┌─────────┐     ┌─────────┐        │  │ Info          │    │
│  ⭐ Favs     │  │ SQL     │────▶│ JSONata │        │  │ Template      │    │
│  🤖 AI       │  └─────────┘     └─────────┘        │  └───────────────┘    │
│  ⚖️ Rule     │                                      │                       │
│  📊 Data     │  [MiniMap]  [Controls]               │  ┌───────────────┐    │
│  🔌 API      │                                      │  │ Validation ✓  │    │
│  ⚙️ Transform│                                      │  └───────────────┘    │
│  🔧 Utility  │                                      │                       │
│  📦 SubFlow  │                                      │  [Save]  [Delete]     │
├──────────────┴──────────────────────────────────────┴───────────────────────┤
│  STATUS BAR: [Zoom: 100%] [Nodes: 12] [Edges: 10] [Valid ✓] [Idle]         │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Adding Nodes

1. Open the **Node Palette** on the left panel
2. Browse categories or use the **search bar** to find a node type
3. **Click** to add to canvas center, or **drag** to a specific position
4. Click ⭐ to mark a node as a favorite for quick access

### Connecting Nodes

1. Hover over a node's **output handle** (right-side dot) — it highlights
2. **Click and drag** to an **input handle** (left-side dot) on another node
3. A connection line appears — nodes are now linked
4. Click an edge to select it; press `Delete` to remove it

### Configuring Nodes

1. **Click a node** on the canvas to select it
2. The **Property Panel** opens on the right with three tabs:
   - **Config** — Dynamic form fields (dropdowns, code editors, toggles, sliders)
   - **Info** — Node metadata (schema ID, category, creation time)
   - **Template** — Save the current configuration as a reusable template
3. Changes apply immediately — no save button needed

### Auto-Layout

Click the **Layout** button (or press `Ctrl+Shift+F`) to automatically arrange nodes using the ELKJS hierarchical layout engine.

---

## 3. State Types

StepFlow implements the full **Amazon States Language** with eight state types. Every node on the canvas maps to one of these states:

### Task

Invokes an external resource to perform work. The `Resource` field specifies what to call using a URI scheme.

```json
{
  "Comment": "Call an AI decision service",
  "Type": "Task",
  "Resource": "ai://decision",
  "Parameters": {
    "question": "Is this order fraudulent?",
    "context.$": "$.order"
  },
  "ResultPath": "$.aiResult",
  "Next": "CheckResult"
}
```

**Key fields:**
| Field | Required | Description |
|---|---|---|
| `Resource` | Yes | URI identifying the service to invoke |
| `Parameters` | No | Input template sent to the resource |
| `ResultPath` | No | Where to store the result in the payload (default: replaces entire payload) |
| `TimeoutSeconds` | No | Per-task timeout |
| `Retry` | No | Retry configuration on failure |
| `Catch` | No | Error routing configuration |

### Pass

Passes input through unchanged, or returns a static value. Useful for testing, injecting fixed data, or simplifying complex payloads.

```json
{
  "Type": "Pass",
  "Result": { "status": "ready", "count": 0 },
  "Next": "ProcessData"
}
```

**Key fields:**
| Field | Required | Description |
|---|---|---|
| `Result` | No | Static value to return (if omitted, passes input through) |
| `ResultPath` | No | Where to merge the result |

### Choice

Routes execution to different paths based on conditions. Evaluates rules top-to-bottom and follows the first match.

```json
{
  "Type": "Choice",
  "Choices": [
    {
      "Variable": "$.order.amount",
      "NumericGreaterThanEquals": 1000,
      "Next": "ManualReview"
    },
    {
      "Variable": "$.order.amount",
      "NumericGreaterThanEquals": 100,
      "Next": "AutoProcess"
    }
  ],
  "Default": "RejectOrder"
}
```

**Key fields:**
| Field | Required | Description |
|---|---|---|
| `Choices` | Yes | Array of condition rules |
| `Default` | No | Fallback state if no choice matches (omit to fail on no match) |

### Wait

Pauses execution for a specified duration before continuing.

```json
{
  "Type": "Wait",
  "Seconds": 30,
  "Next": "RetryCheck"
}
```

**Key fields:**
| Field | Required | Description |
|---|---|---|
| `Seconds` | One of | Fixed wait in seconds |
| `Timestamp` | One of | Wait until this ISO 8601 timestamp |
| `SecondsPath` | One of | JSONPath to a numeric value in the input |
| `TimestampPath` | One of | JSONPath to a timestamp value in the input |

### Succeed

Terminates the state machine with a successful status.

```json
{
  "Type": "Succeed"
}
```

No `Next` field is needed — this is always a terminal state.

### Fail

Terminates the state machine with an error.

```json
{
  "Type": "Fail",
  "Error": "OrderRejected",
  "Cause": "Order amount exceeds customer credit limit"
}
```

**Key fields:**
| Field | Required | Description |
|---|---|---|
| `Error` | Yes | Error code string |
| `Cause` | No | Human-readable error description |

### Parallel

Executes multiple independent branches concurrently. All branches must complete successfully for the Parallel state to succeed.

```json
{
  "Type": "Parallel",
  "Branches": [
    {
      "StartAt": "CheckInventory",
      "States": { "CheckInventory": { "Type": "Task", "Resource": "rule://InventoryCheck", "End": true } }
    },
    {
      "StartAt": "ValidatePayment",
      "States": { "ValidatePayment": { "Type": "Task", "Resource": "rule://PaymentValidate", "End": true } }
    }
  ],
  "Next": "ShipOrder"
}
```

**Key fields:**
| Field | Required | Description |
|---|---|---|
| `Branches` | Yes | Array of nested state machines (each with `StartAt` and `States`) |
| `MaxConcurrency` | No | Maximum number of simultaneous branch executions |

### Map

Iterates over an array and runs a sub-state machine for each item.

```json
{
  "Type": "Map",
  "ItemsPath": "$.orders",
  "Iterator": {
    "StartAt": "ProcessOrder",
    "States": {
      "ProcessOrder": {
        "Type": "Task",
        "Resource": "rule://ProcessSingleOrder",
        "End": true
      }
    }
  },
  "Next": "SummarizeResults"
}
```

**Key fields:**
| Field | Required | Description |
|---|---|---|
| `ItemsPath` | Yes | JSONPath to the array to iterate over |
| `Iterator` | Yes | Nested state machine run for each item |
| `MaxConcurrency` | No | Maximum parallel iterations |

---

## 4. Resource Schemes

`Task` states use URI schemes to route work to the correct handler. Here are all supported schemes:

### AI Resources (`ai://`)

Invoke the AI Decision Service for LLM-powered decisions.

| Resource | Description |
|---|---|
| `ai://decision` | Ask the AI a yes/no question with reasoning |
| `ai://decide/<provider>` | Specify an AI provider (e.g., `ai://decide/LMStudio`) |

**Input format:**
```json
{
  "question": "Is this transaction suspicious?",
  "context": { "amount": 5000, "location": "unknown" },
  "confidenceThreshold": 0.8
}
```

**Output format:**
```json
{
  "decision": true,
  "answer": "Yes, this appears suspicious due to...",
  "reasoning": "The amount is high and location is unverified",
  "confidence": 0.92,
  "meetsThreshold": true
}
```

### Rule Engine (`rule://`)

Execute business rules via the NRules engine.

| Resource | Description |
|---|---|
| `rule://<ruleId>` | Execute a specific rule by ID |
| `rule://<ruleId>?eav=<entity>` | Execute with EAV entity mapping |

**Example:**
```json
{
  "Type": "Task",
  "Resource": "rule://CheckCredit?eav=Customer",
  "Parameters": {
    "customerName": "John Doe",
    "creditLimit.$": "$.customer.creditLimit"
  }
}
```

### Microsoft RulesEngine (`rules://`)

Execute Microsoft RulesEngine workflows.

| Resource | Description |
|---|---|
| `rules://<workflowName>` | Execute a named workflow |

**Example:**
```json
{
  "Type": "Task",
  "Resource": "rules://ComplianceWorkflow",
  "Parameters": {
    "documentType": "invoice",
    "amount": 2500
  }
}
```

### DuckDB Transform (`transform://`)

Run SQL queries and data transformations via DuckDB.

| Resource | Description |
|---|---|
| `transform://query` | Execute a SQL query |
| `transform://<operation>` | Execute a named operation |

**Input format:**
```json
{
  "operation": "query",
  "sql": "SELECT * FROM orders WHERE amount > ?",
  "parameters": [1000]
}
```

### Sub-Flow Invocation (`flow://`)

Call another state machine as a nested flow.

| Resource | Description |
|---|---|
| `flow://<flowId>` | Execute a saved flow by ID |

**Example:**
```json
{
  "Type": "Task",
  "Resource": "flow://CustomerValidation",
  "Parameters": {
    "customerId.$": "$.customer.id"
  }
}
```

### HTTP Resources (`http://`, `https://`)

Make HTTP requests to external APIs.

**Simple mode** (POST with input as body):
```json
{
  "Type": "Task",
  "Resource": "https://api.example.com/charge",
  "Parameters": {
    "amount": 99.99,
    "currency": "USD"
  }
}
```

**Structured mode** (full control over method, headers, auth):
```json
{
  "Type": "Task",
  "Resource": "https://api.example.com/orders",
  "Parameters": {
    "__handler": "http",
    "method": "GET",
    "query": { "status": "pending", "page": "1" },
    "auth": {
      "type": "Bearer",
      "token": "eyJhbG..."
    }
  }
}
```

### Tool Resources (`tool://`)

Invoke registered tools via the callback endpoint.

| Resource | Description |
|---|---|
| `tool://<toolName>` | Execute a named tool |

### Internal Resources (`internal://`)

Built-in diagnostic and utility endpoints.

| Resource | Description |
|---|---|
| `internal://echo` | Returns the input unchanged |
| `internal://engine/status` | Rule engine status |
| `internal://rules/status` | MS RulesEngine status |
| `internal://transform/status` | DuckDB transform status |

---

## 5. Choice Rules

Choice states evaluate conditions top-to-bottom. The first matching rule determines the next state.

### Comparison Operators

#### String Comparisons

```json
{ "Variable": "$.user.role", "StringEquals": "admin", "Next": "AdminPanel" }
{ "Variable": "$.user.role", "StringGreaterThan": "manager", "Next": "ElevatedAccess" }
{ "Variable": "$.user.email", "StringMatches": "*.company.com", "Next": "InternalRoute" }
```

| Operator | Description |
|---|---|
| `StringEquals` | Exact string match |
| `StringEqualsPath` | Compare against another path in the input |
| `StringGreaterThan` | Lexicographic greater-than |
| `StringLessThan` | Lexicographic less-than |
| `StringMatches` | Wildcard pattern (`*` matches any characters) |

#### Numeric Comparisons

```json
{ "Variable": "$.order.amount", "NumericGreaterThanEquals": 1000, "Next": "Review" }
{ "Variable": "$.inventory.count", "NumericLessThan": 10, "Next": "Reorder" }
```

| Operator | Description |
|---|---|
| `NumericEquals` | Exact number match |
| `NumericGreaterThan` | Strictly greater than |
| `NumericGreaterThanEquals` | Greater than or equal |
| `NumericLessThan` | Strictly less than |
| `NumericLessThanEquals` | Less than or equal |

#### Boolean Comparisons

```json
{ "Variable": "$.flags.isVerified", "BooleanEquals": true, "Next": "Process" }
```

| Operator | Description |
|---|---|
| `BooleanEquals` | Check if the value equals `true` or `false` |

#### Timestamp Comparisons

```json
{ "Variable": "$.order.date", "TimestampGreaterThan": "2025-01-01T00:00:00Z", "Next": "CurrentYear" }
```

| Operator | Description |
|---|---|
| `TimestampEquals` | Exact timestamp match |
| `TimestampGreaterThan` | After the given timestamp |
| `TimestampLessThan` | Before the given timestamp |

#### Type and Presence Checks

```json
{ "Variable": "$.optionalField", "IsPresent": true, "Next": "HasValue" }
{ "Variable": "$.optionalField", "IsNull": true, "Next": "MissingValue" }
{ "Variable": "$.field", "IsString": true, "Next": "StringBranch" }
```

| Operator | Description |
|---|---|
| `IsPresent` | Value exists and is not null |
| `IsNull` | Value is null or missing |
| `IsString` | Value is a string type |
| `IsNumeric` | Value is a number type |
| `IsBoolean` | Value is a boolean type |

### Logical Operators

Combine multiple conditions using `And`, `Or`, and `Not`:

```json
{
  "And": [
    { "Variable": "$.order.amount", "NumericGreaterThan": 100 },
    { "Variable": "$.customer.verified", "BooleanEquals": true }
  ],
  "Next": "FastTrack"
}
```

```json
{
  "Or": [
    { "Variable": "$.user.role", "StringEquals": "admin" },
    { "Variable": "$.user.role", "StringEquals": "superuser" }
  ],
  "Next": "FullAccess"
}
```

```json
{
  "Not": {
    "Variable": "$.flags.isBlocked",
    "BooleanEquals": true
  },
  "Next": "AllowProcessing"
}
```

---

## 6. Data Flow and Payload Handling

Every state can transform data as it flows through the pipeline. Four fields control data shaping:

### InputPath

Restricts what portion of the input the state receives.

```json
{
  "Type": "Task",
  "InputPath": "$.order",
  "Resource": "rule://ValidateOrder"
}
```

Only the `order` object is passed to the task — the rest of the payload is preserved.

### Parameters

Builds a new input structure from the effective input using payload templates. Fields ending in `.$` resolve JSONPath expressions:

```json
{
  "Type": "Task",
  "Parameters": {
    "customerId": "C-12345",
    "customerName.$": "$.customer.name",
    "orderTotal.$": "$.order.total",
    "timestamp.$": "$$.Execution.StartTime"
  }
}
```

| Prefix | Meaning |
|---|---|
| `$.` | Path in the current input payload |
| `$$.` | Path in the execution context (e.g., `$$.Execution.Id`) |

### ResultPath

Controls where the task result is merged back into the payload:

```json
{
  "Type": "Task",
  "Resource": "ai://decision",
  "ResultPath": "$.aiDecision"
}
```

- `"$"` (default): Result replaces the entire payload
- `"$.aiDecision"`: Result is stored under the `aiDecision` key
- `"$.results[0]"`: Result is stored in an array slot

### OutputPath

Restricts what portion of the output is passed to the next state:

```json
{
  "Type": "Task",
  "Resource": "rule://CheckCredit",
  "OutputPath": "$.result"
}
```

Only the `result` field is forwarded — everything else is stripped.

### ResultSelector

Reshapes the raw result before it is merged via `ResultPath`:

```json
{
  "Type": "Task",
  "Resource": "ai://decision",
  "ResultSelector": {
    "approved.$": "$.meetsThreshold",
    "score.$": "$.confidence",
    "reason.$": "$.reasoning"
  }
}
```

### Processing Order

```
Input → [InputPath] → [Parameters] → Resource → [ResultSelector] → [ResultPath] → [OutputPath] → Next State
```

---

## 7. Error Handling

### Retry Policies

Configure automatic retries with exponential backoff:

```json
{
  "Type": "Task",
  "Resource": "https://api.example.com/process",
  "Retry": [
    {
      "ErrorEquals": ["States.TaskFailed"],
      "IntervalSeconds": 2,
      "MaxAttempts": 3,
      "BackoffRate": 2.0
    },
    {
      "ErrorEquals": ["States.ALL"],
      "IntervalSeconds": 1,
      "MaxAttempts": 5,
      "BackoffRate": 1.5
    }
  ]
}
```

| Field | Default | Description |
|---|---|---|
| `ErrorEquals` | — | Which errors trigger retry (`States.ALL` matches everything) |
| `IntervalSeconds` | 1 | Initial wait before first retry |
| `MaxAttempts` | 3 | Maximum retry attempts |
| `BackoffRate` | 2.0 | Multiplier between retry intervals |

### Catch Rules

Route to a recovery state when retries are exhausted:

```json
{
  "Type": "Task",
  "Resource": "https://api.example.com/process",
  "Catch": [
    {
      "ErrorEquals": ["States.TaskFailed"],
      "Next": "HandlePaymentError",
      "ResultPath": "$.errorDetail"
    },
    {
      "ErrorEquals": ["States.ALL"],
      "Next": "LogAndFail"
    }
  ]
}
```

| Field | Description |
|---|---|
| `ErrorEquals` | Which errors this catcher handles |
| `Next` | State to transition to on error |
| `ResultPath` | Where to store the error info (omit to replace payload) |

### Built-in Error Codes

| Error Code | Cause |
|---|---|
| `States.Runtime` | Invalid state machine definition |
| `States.Timeout` | Task or execution exceeded its timeout |
| `States.TaskFailed` | Resource invocation failed |
| `States.NoChoiceMatched` | No choice rule matched and no `Default` specified |
| `States.BranchFailed` | A parallel branch failed |

---

## 8. Node Categories and Schemas

The palette organizes 15 node types across 7 categories:

### 🤖 AI (Purple — `#8B5CF6`)

| Node | Description |
|---|---|
| **AI Decision** | Yes/no question with confidence scoring and reasoning |
| **AI Text Generation** | Free-form text generation from prompts |

### ⚖️ Rule (Orange — `#F97316`)

| Node | Description |
|---|---|
| **Rule Engine** | NRules business rule evaluation |
| **MS Rules** | Microsoft RulesEngine workflow execution |

### 📊 Data (Blue — `#3B82F6`)

| Node | Description |
|---|---|
| **SQL Query** | Execute SQL against DuckDB |
| **DuckDB** | Advanced DuckDB analytics operations |
| **EAV** | Entity-Attribute-Value registry lookups |

### 🔌 API (Green — `#22C55E`)

| Node | Description |
|---|---|
| **HTTP Request** | Generic HTTP calls with method, auth, and query support |
| **Registered API** | Pre-configured API endpoints from the registry |

### ⚙️ Transform (Yellow — `#EAB308`)

| Node | Description |
|---|---|
| **JSONata** | JSONata expression evaluation |
| **Script** | Custom script transformations |

### 🔧 Utility (Gray — `#6B7280`)

| Node | Description |
|---|---|
| **Pass** | Pass-through or static value injection |
| **Wait** | Delay execution |
| **Branch** | Parallel branch execution |

### 📦 SubFlow (Cyan — `#06B6D4`)

| Node | Description |
|---|---|
| **Sub-Flow Invoke** | Call another saved flow |

---

## 9. Running Flows

### Execution Steps

1. Click the **Run** button (▶) in the toolbar, or press `Ctrl+Enter`
2. Nodes light up as they execute — watch the animated progress on the canvas
3. Click **Stop** (⏹) at any time to halt execution
4. Check the **status bar** for execution state: `Idle`, `Running`, `Succeeded`, `Failed`, or `TimedOut`

### Execution Lifecycle

```
Starting → StateEntered → StateExited → ... → Succeeded / Failed / TimedOut
```

### Timeouts

Set a global timeout on the state machine:

```json
{
  "Version": "1.0",
  "TimeoutSeconds": 300,
  "StartAt": "FirstState",
  "States": { ... }
}
```

Or per-task timeouts:

```json
{
  "Type": "Task",
  "Resource": "https://slow-api.example.com/data",
  "TimeoutSeconds": 30
}
```

### Query Language

The system supports two query languages for path resolution:

| Setting | Value | Description |
|---|---|---|
| `QueryLanguage` | `"JSONPath"` (default) | Standard JSONPath expressions |
| `QueryLanguage` | `"JSONata"` | [JSONata](https://jsonata.org/) expressions for advanced transformations |

---

## 10. Saving and Loading Flows

### Save

Click **Save** (or press `Ctrl+S`). The flow is persisted to `localStorage` and can be exported as Amazon States Language JSON.

### Load

Access saved flows from the flow list in the header menu.

### Export

Flows can be exported as standard ASL JSON for deployment to AWS Step Functions or other compatible runtimes.

---

## 11. Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl + Z` | Undo |
| `Ctrl + Y` | Redo |
| `Ctrl + S` | Save flow |
| `Ctrl + Enter` | Run / Stop execution |
| `Ctrl + Shift + F` | Auto-arrange nodes |
| `Delete` | Delete selected node(s) |
| `Escape` | Deselect all |

---

## 12. Importing Legacy Flows

The **Flow Migration Service** converts existing ASL JSON files from the `Flows/` directory into canvas nodes and edges.

### Available Legacy Flows

| Flow | Description |
|---|---|
| `OrderProcess` | Order processing with credit checks and AI review |
| `REG_INT_BTP` | Bank file to authority payments pipeline |
| `ParentFlow` | Sub-flow composition example |
| `WebBrowserDemo` | Web automation with AI analysis |

### Import Steps

1. Place your ASL JSON file in the `Flows/` directory
2. Use the migration tool to convert it to canvas nodes
3. Edit and refine in the visual builder

---

## 13. Examples

### Example 1: Order Processing Pipeline

```json
{
  "Comment": "Process an order with validation, AI review, and fulfillment",
  "StartAt": "ValidateOrder",
  "States": {
    "ValidateOrder": {
      "Type": "Task",
      "Resource": "rule://CheckCredit?eav=Order",
      "Parameters": {
        "orderId.$": "$.id",
        "amount.$": "$.amount",
        "customerId.$": "$.customerId"
      },
      "ResultPath": "$.validation",
      "Next": "IsApproved",
      "Catch": [
        { "ErrorEquals": ["States.ALL"], "Next": "LogError" }
      ]
    },
    "IsApproved": {
      "Type": "Choice",
      "Choices": [
        {
          "Variable": "$.validation.approved",
          "BooleanEquals": true,
          "Next": "HighValueReview"
        },
        {
          "Variable": "$.validation.approved",
          "BooleanEquals": false,
          "Next": "RejectOrder"
        }
      ],
      "Default": "RejectOrder"
    },
    "HighValueReview": {
      "Type": "Choice",
      "Choices": [
        {
          "Variable": "$.amount",
          "NumericGreaterThanEquals": 1000,
          "Next": "AIReview"
        }
      ],
      "Default": "ProcessOrder"
    },
    "AIReview": {
      "Type": "Task",
      "Resource": "ai://decision",
      "Parameters": {
        "question": "Should this high-value order be approved?",
        "context.$": "$"
      },
      "ResultPath": "$.aiReview",
      "Next": "AIApproved"
    },
    "AIApproved": {
      "Type": "Choice",
      "Choices": [
        {
          "Variable": "$.aiReview.meetsThreshold",
          "BooleanEquals": true,
          "Next": "ProcessOrder"
        }
      ],
      "Default": "ManualReview"
    },
    "ProcessOrder": {
      "Type": "Task",
      "Resource": "transform://process",
      "Next": "SendConfirmation"
    },
    "SendConfirmation": {
      "Type": "Task",
      "Resource": "https://notifications.example.com/send",
      "Parameters": {
        "__handler": "http",
        "method": "POST",
        "body": {
          "orderId.$": "$.id",
          "customerEmail.$": "$.customerEmail",
          "status": "confirmed"
        }
      },
      "End": true
    },
    "RejectOrder": {
      "Type": "Fail",
      "Error": "OrderRejected",
      "Cause": "Order did not pass validation"
    },
    "ManualReview": {
      "Type": "Succeed"
    },
    "LogError": {
      "Type": "Task",
      "Resource": "internal://echo",
      "End": true
    }
  }
}
```

### Example 2: Parallel Data Processing

```json
{
  "Comment": "Process multiple data sources in parallel",
  "StartAt": "FetchData",
  "States": {
    "FetchData": {
      "Type": "Parallel",
      "Branches": [
        {
          "StartAt": "GetOrders",
          "States": {
            "GetOrders": {
              "Type": "Task",
              "Resource": "transform://query",
              "Parameters": { "sql": "SELECT * FROM orders" },
              "End": true
            }
          }
        },
        {
          "StartAt": "GetCustomers",
          "States": {
            "GetCustomers": {
              "Type": "Task",
              "Resource": "transform://query",
              "Parameters": { "sql": "SELECT * FROM customers" },
              "End": true
            }
          }
        },
        {
          "StartAt": "GetInventory",
          "States": {
            "GetInventory": {
              "Type": "Task",
              "Resource": "transform://query",
              "Parameters": { "sql": "SELECT * FROM inventory" },
              "End": true
            }
          }
        }
      ],
      "ResultPath": "$.data",
      "Next": "MergeResults"
    },
    "MergeResults": {
      "Type": "Task",
      "Resource": "transform://merge",
      "Parameters": {
        "datasets.$": "$.data"
      },
      "End": true
    }
  }
}
```

### Example 3: Retry with Exponential Backoff

```json
{
  "Comment": "Call external API with retry logic",
  "StartAt": "CallExternalAPI",
  "States": {
    "CallExternalAPI": {
      "Type": "Task",
      "Resource": "https://api.example.com/process",
      "Parameters": {
        "__handler": "http",
        "method": "POST",
        "body": {
          "data.$": "$.payload"
        }
      },
      "TimeoutSeconds": 30,
      "Retry": [
        {
          "ErrorEquals": ["States.TaskFailed"],
          "IntervalSeconds": 2,
          "MaxAttempts": 4,
          "BackoffRate": 2.0
        }
      ],
      "Catch": [
        {
          "ErrorEquals": ["States.ALL"],
          "Next": "FallbackHandler"
        }
      ],
      "Next": "Success"
    },
    "FallbackHandler": {
      "Type": "Task",
      "Resource": "transform://fallback",
      "End": true
    },
    "Success": {
      "Type": "Succeed"
    }
  }
}
```

---

*For API reference, testing details, and architectural information, see [README.md](./README.md).*
