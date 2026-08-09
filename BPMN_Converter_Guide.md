# BPMN → StepFlow Converter

End-to-end guide for importing Camunda 8 (Zeebe) BPMN XML files and running them as StepFunctions state machines.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Conversion Pipeline](#2-conversion-pipeline)
3. [BPMN → StepFlow Element Mapping](#3-bpmn--stepflow-element-mapping)
4. [Resource Resolution](#4-resource-resolution)
5. [Condition Expression Mapping](#5-condition-expression-mapping)
6. [API Endpoints](#6-api-endpoints)
7. [Usage Examples](#7-usage-examples)
8. [Parallel & Multi-Instance](#8-parallel--multi-instance)
9. [Auto-Loading from Flows Directory](#9-auto-loading-from-flows-directory)
10. [Troubleshooting](#10-troubleshooting)

---

## 1. Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                        BPMN XML File                             │
│  (Camunda 7 / Camunda 8 Zeebe / standard BPMN 2.0)             │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                    BpmnConverter                                 │
│                                                                  │
│  ┌────────────────┐    ┌───────────────────┐                    │
│  │ XmlSerializer  │───▶│ XDocument Enrich  │                    │
│  │ (BPMN models)  │    │ (zeebe/camunda    │                    │
│  └────────────────┘    │  namespace props) │                    │
│                        └───────────────────┘                    │
│                                 │                                │
│                                 ▼                                │
│                        ┌───────────────────┐                    │
│                        │ State Machine     │                    │
│                        │ Builder           │                    │
│                        │ (ASL conversion)  │                    │
│                        └───────────────────┘                    │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│              StateMachineDefinition (JSON)                       │
│                                                                  │
│  {                                                               │
│    "startAt": "StartEvent_1",                                    │
│    "states": {                                                   │
│      "ValidateOrder": {                                         │
│        "type": "Task",                                          │
│        "resource": "http://validateorder",                       │
│        "next": "CheckCredit"                                    │
│      },                                                         │
│      ...                                                         │
│    }                                                             │
│  }                                                               │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│              StepFunctionInterpreter                             │
│                                                                  │
│  Executes against:                                               │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐                │
│  │ http://... │  │ rule://... │  │ ai://...   │                │
│  │ (HTTP API) │  │ (SQL rules)│  │ (LLM call) │                │
│  └────────────┘  └────────────┘  └────────────┘                │
│  ┌────────────┐  ┌────────────┐                                 │
│  │ flow://... │  │internal:// │                                 │
│  │ (sub-flow) │  │ (builtin)  │                                 │
│  └────────────┘  └────────────┘                                 │
└──────────────────────────────────────────────────────────────────┘
```

---

## 2. Conversion Pipeline

The converter runs in two phases:

### Phase 1: XML Deserialization

Uses `XmlSerializer` to parse the BPMN XML into strongly-typed C# models (`BpmnProcess`, `BpmnServiceTask`, etc.).

**Limitation:** `XmlSerializer` cannot handle zeebe/camunda namespace elements nested inside `<extensionElements>`. The service task's `ZeebeTaskDefinition`, `ZeebeIoMapping`, and property lists come back as `null`.

### Phase 2: XDocument Enrichment

Re-parses the raw XML with `XDocument` (LINQ to XML) to extract zeebe and camunda namespace attributes that XmlSerializer missed:

| XML Element | Extracted Property | Used For |
|---|---|---|
| `zeebe:taskDefinition type="..."` | `ZeebeTaskDefinition.Type` | Resource scheme detection |
| `zeebe:properties` → `property name="url" value="..."` | Extension properties | HTTP URL resolution |
| `camunda:properties` → `property name="url" value="..."` | Extension properties | HTTP URL resolution |
| `zeebe:ioMapping` → `zeebe:input` / `zeebe:output` | `ZeebeIoMapping` | Parameters / ResultPath |
| `conditionExpression` on sequence flows | `SequenceFlow.ConditionExpression` | Choice rule generation |
| `zeebe:properties` → `property name="decisionId"` | Decision ID | Rule engine mapping |

### Phase 3: State Machine Construction

Each BPMN element is converted to an Amazon States Language state:

```
BPMN Process
  ├── StartEvent        → Pass state (empty result)
  ├── ServiceTask       → Task state (with resolved resource)
  ├── ExclusiveGateway  → Choice state (with choice rules)
  ├── ParallelGateway   → Parallel state (with branches)
  ├── EndEvent          → Succeed state
  ├── ScriptTask        → Pass state (script embedded in result)
  ├── ReceiveTask       → Wait state (placeholder)
  └── SendTask          → Task or Pass state
```

---

## 3. BPMN → StepFlow Element Mapping

### Start Events

```xml
<startEvent id="StartOrder" name="Order Received" />
```

```json
{
  "StartOrder": {
    "type": "Pass",
    "comment": "Start Event: Order Received",
    "result": {},
    "next": "ValidateOrder"
  }
}
```

### Service Tasks

```xml
<serviceTask id="ValidateOrder" name="Validate Order">
  <extensionElements>
    <zeebe:taskDefinition type="http-call" />
  </extensionElements>
</serviceTask>
```

```json
{
  "ValidateOrder": {
    "type": "Task",
    "comment": "Service Task: Validate Order",
    "resource": "http://validateorder",
    "next": "CheckCredit"
  }
}
```

### Exclusive Gateways (XOR)

```xml
<exclusiveGateway id="CheckCredit" name="Credit Decision" />
<sequenceFlow sourceRef="CheckCredit" targetRef="Approve">
  <conditionExpression>creditScore > 650</conditionExpression>
</sequenceFlow>
<sequenceFlow sourceRef="CheckCredit" targetRef="Reject" />
```

```json
{
  "CheckCredit": {
    "type": "Choice",
    "comment": "Exclusive Gateway: Credit Decision",
    "choices": [
      {
        "variable": "$.creditScore",
        "numericGreaterThan": 650,
        "next": "Approve"
      }
    ],
    "default": "Reject"
  }
}
```

### End Events

```xml
<endEvent id="Done" name="Order Complete" />
```

```json
{
  "Done": {
    "type": "Succeed",
    "comment": "End Event: Order Complete",
    "end": true
  }
}
```

---

## 4. Resource Resolution

Service tasks are mapped to resource URIs based on the zeebe task type and extension properties.

### Resolution Order

```
1. zeebe:taskDefinition type  →  scheme detection
2. camunda:calledElement      →  flow://
3. Extension properties       →  url/endpoint/uri value
4. Process-level properties   →  resourceMapping
5. Task name as URL           →  http://{taskname}
6. Fallback                   →  internal://echo
```

### Zeebe Type → Scheme Mapping

| Zeebe Type (contains) | Resource Scheme | Example |
|---|---|---|
| `http`, `rest`, `api`, `webhook` | `http://` or extracted URL | `http://validateorder` |
| `ai`, `llm`, `prompt`, `openai`, `gpt` | `ai://decision` | AI decision call |
| `decision`, `dmn`, `rule` | `rule://{decisionId}` | SQL rule evaluation |
| `flow`, `process`, `subprocess` | `flow://{name}` | Sub-flow invocation |

### URL Extraction from Properties

The converter checks extension properties for URL values:

```xml
<serviceTask id="ProcessPayment" name="Process Payment">
  <extensionElements>
    <zeebe:taskDefinition type="http-call" />
    <camunda:properties>
      <camunda:property name="url" value="https://api.payment.example.com/charge" />
    </camunda:properties>
  </extensionElements>
</serviceTask>
```

→ `resource: "https://api.payment.example.com/charge"`

Property names checked: `url`, `httpUrl`, `endpoint`, `uri`, `resource`

### Zeebe IO Mapping → Parameters / ResultPath

```xml
<serviceTask id="ValidateOrder" name="Validate Order">
  <extensionElements>
    <zeebe:taskDefinition type="http-call" />
    <zeebe:ioMapping>
      <zeebe:input source="orderData" target="payload" />
      <zeebe:output source="result" target="validated" />
    </zeebe:ioMapping>
  </extensionElements>
</serviceTask>
```

```json
{
  "ValidateOrder": {
    "type": "Task",
    "resource": "http://validateorder",
    "parameters": { "payload": "$.orderData" },
    "resultPath": "$.validated"
  }
}
```

---

## 5. Condition Expression Mapping

BPMN uses FEEL (Friendly Enough Expression Language) for gateway conditions. The converter parses common FEEL patterns into ASL ChoiceRules.

### Supported Patterns

| FEEL Expression | ASL ChoiceRule | Example |
|---|---|---|
| `var == "value"` | `StringEquals` | `status == "approved"` |
| `var == number` | `NumericEquals` | `score == 100` |
| `var == true/false` | `BooleanEquals` | `isValid == true` |
| `var > number` | `NumericGreaterThan` | `score > 650` |
| `var >= number` | `NumericGreaterThanEquals` | `age >= 18` |
| `var < number` | `NumericLessThan` | `amount < 100` |
| `var <= number` | `NumericLessThanEquals` | `count <= 10` |
| `var > 0` | `NumericGreaterThan: 0` | `items > 0` |
| `contains(var, "text")` | `StringMatches: "*text*"` | `contains(name, "John")` |
| `startsWith(var, "prefix")` | `StringMatches: "prefix*"` | `startsWith(code, "ERR")` |

### Variable Resolution

BPMN variable names are prefixed with `$.` to form JSONPath expressions:

```
creditScore > 650  →  variable: "$.creditScore", numericGreaterThan: 650
```

### Unparsed Conditions

If a FEEL expression cannot be parsed, it falls back to a string equality check with a debug log entry:

```
[DBG] Could not fully parse BPMN condition 'x and (y > 5)', using fallback
```

---

## 6. API Endpoints

### POST `/api/bpmn/convert`

Converts BPMN XML to a StepFlow state machine definition **without** registering it.

**Request:**
```bash
curl -X POST http://localhost:5001/api/bpmn/convert \
  -H "Content-Type: application/xml" \
  -d @my-process.bpmn
```

**Response:**
```json
{
  "stateMachine": {
    "comment": "Converted from BPMN process 'OrderProcess' (Order Processing Workflow)",
    "startAt": "StartEvent_1",
    "states": { ... }
  }
}
```

---

### POST `/api/bpmn/import`

Uploads a `.bpmn` file, converts it, registers the state machine, and persists it as a `.json` file in the Flows directory.

**Request:**
```bash
curl -X POST http://localhost:5001/api/bpmn/import \
  -F "file=@my-process.bpmn"
```

**Response:**
```json
{
  "id": "a1b2c3d4e5f6",
  "name": "my-process",
  "stateCount": 12
}
```

The converted definition is saved to `Flows/my-process.json` and can be used with all existing StepFlow APIs.

---

### POST `/api/bpmn/import-and-execute`

Uploads, converts, registers, and immediately executes the flow.

**Request:**
```bash
curl -X POST http://localhost:5001/api/bpmn/import-and-execute \
  -F "file=@my-process.bpmn" \
  -F "inputJson={\"creditScore\": 700, \"amount\": 150}"
```

**Response:**
```json
{
  "executionId": "abc123def456",
  "status": "Succeeded",
  "output": { ... },
  "history": [ ... ]
}
```

---

### Existing StepFlow APIs (work with BPMN-converted flows)

| Endpoint | Method | Description |
|---|---|---|
| `/api/flows` | GET | List all registered state machines |
| `/api/flows/{id}/definition` | GET | Get state machine definition |
| `/api/flows/execute/{id}` | POST | Start async execution |
| `/api/flows/execute-sync/{id}` | POST | Execute synchronously |
| `/api/flows/executions/{id}` | GET | Get execution status |

---

## 7. Usage Examples

### Example 1: Convert and Inspect

```bash
# Convert a BPMN file and see the generated state machine
curl -s -X POST http://localhost:5001/api/bpmn/convert \
  -H "Content-Type: application/xml" \
  -d @Flows/OrderProcess.bpmn | jq '.stateMachine.states | keys'
```

Output:
```
[
  "StartEvent_1",
  "ValidateOrder",
  "CheckCredit",
  "ApproveOrder",
  "RunRules",
  "ProcessPayment",
  "SendConfirmation",
  "AiReview",
  "RejectOrder",
  "EndEvent_1",
  "EndEvent_2"
]
```

---

### Example 2: Import and Execute

```bash
# Step 1: Import the BPMN file
curl -X POST http://localhost:5001/api/bpmn/import \
  -F "file=@Flows/OrderProcess.bpmn"

# Step 2: Verify it's registered
curl http://localhost:5001/api/flows

# Step 3: Execute with input data
curl -X POST http://localhost:5001/api/flows/execute-sync/OrderProcess \
  -H "Content-Type: application/json" \
  -d '{"creditScore": 700, "orderId": "ORD-123"}'
```

---

### Example 3: BPMN with AI and Rules

This BPMN demonstrates a service task calling AI, then a rule engine:

```xml
<process id="FraudCheck" name="Fraud Detection Pipeline">
  <startEvent id="Start" />

  <serviceTask id="AiAnalyze" name="AI Fraud Analysis">
    <extensionElements>
      <zeebe:taskDefinition type="ai-prompt" />
      <zeebe:ioMapping>
        <zeebe:input source="transaction" target="context" />
      </zeebe:ioMapping>
    </extensionElements>
  </serviceTask>

  <serviceTask id="RuleCheck" name="Compliance Rules">
    <extensionElements>
      <zeebe:taskDefinition type="decision" />
      <zeebe:properties>
        <zeebe:property name="decisionId" value="FraudRule" />
      </zeebe:properties>
    </extensionElements>
  </serviceTask>

  <exclusiveGateway id="Decision" />

  <sequenceFlow sourceRef="Decision" targetRef="Approve">
    <conditionExpression>isFraud == false</conditionExpression>
  </sequenceFlow>

  <sequenceFlow sourceRef="Decision" targetRef="Block" />

  <serviceTask id="Approve" name="Approve Transaction">
    <extensionElements>
      <zeebe:taskDefinition type="http-call" />
      <camunda:properties>
        <camunda:property name="url" value="https://api.example.com/approve" />
      </camunda:properties>
    </extensionElements>
  </serviceTask>

  <serviceTask id="Block" name="Block Transaction">
    <extensionElements>
      <zeebe:taskDefinition type="http-call" />
      <camunda:properties>
        <camunda:property name="url" value="https://api.example.com/block" />
      </camunda:properties>
    </extensionElements>
  </serviceTask>

  <endEvent id="Done" />

  <!-- Sequence flows connecting everything -->
  <sequenceFlow sourceRef="Start" targetRef="AiAnalyze" />
  <sequenceFlow sourceRef="AiAnalyze" targetRef="RuleCheck" />
  <sequenceFlow sourceRef="RuleCheck" targetRef="Decision" />
  <sequenceFlow sourceRef="Approve" targetRef="Done" />
  <sequenceFlow sourceRef="Block" targetRef="Done" />
</process>
```

Converted StepFlow resource chain:
```
AiAnalyze    → ai://decision        (LLM analysis)
RuleCheck    → rule://FraudRule     (SQL rule evaluation)
Approve      → https://api.example.com/approve
Block        → https://api.example.com/block
```

---

### Example 4: Sub-Flow Composition

Call another registered flow from within a BPMN:

```xml
<serviceTask id="RunSubFlow" name="Run Notification Flow">
  <extensionElements>
    <zeebe:taskDefinition type="flow" />
  </extensionElements>
</serviceTask>
```

→ `resource: "flow://RunSubFlow"` — invokes the `RunSubFlow` state machine if it exists.

---

## 8. Parallel & Multi-Instance

### Parallel Gateways

```xml
<parallelGateway id="Split" />
<sequenceFlow sourceRef="Split" targetRef="TaskA" />
<sequenceFlow sourceRef="Split" targetRef="TaskB" />
<parallelGateway id="Merge" />
<sequenceFlow sourceRef="TaskA" targetRef="Merge" />
<sequenceFlow sourceRef="TaskB" targetRef="Merge" />
```

```json
{
  "Split": {
    "type": "Parallel",
    "branches": [
      { "startAt": "TaskA", "states": { "TaskA": { ... } } },
      { "startAt": "TaskB", "states": { "TaskB": { ... } } }
    ],
    "next": "Merge"
  }
}
```

The converter traces each branch from the split gateway to the merge point, building independent sub-state-machines for each branch.

### Multi-Instance (Map)

```xml
<serviceTask id="ProcessItems" name="Process Each Item">
  <extensionElements>
    <zeebe:taskDefinition type="http-call" />
  </extensionElements>
  <multiInstance collection="items" elementVariable="item" />
</serviceTask>
```

```json
{
  "ProcessItems": {
    "type": "Map",
    "itemsPath": "$.items",
    "iterator": {
      "startAt": "MapItem",
      "states": {
        "MapItem": {
          "type": "Task",
          "resource": "http://...",
          "end": true
        }
      }
    },
    "next": "NextState"
  }
}
```

---

## 9. Auto-Loading from Flows Directory

On startup, `StepFunctionService.ReloadFromDirectory()` scans the `Flows/` directory for:

| Extension | Behavior |
|---|---|
| `*.json` | Loaded directly as a `StateMachineDefinition` |
| `*.bpmn` | Parsed, converted via `BpmnConverter`, registered as a state machine |

The converted BPMN is **not** persisted back to disk on startup (to avoid overwriting). Use the `/api/bpmn/import` endpoint to persist.

To reload flows at runtime:

```bash
curl -X POST http://localhost:5001/api/flows/reload
```

---

## 10. Troubleshooting

### "No process found" error

Ensure the BPMN XML has a `<process>` element with `isExecutable="true"` (or any process element — the converter uses the first one).

### Resources resolve to `internal://echo`

The zeebe task type wasn't recognized and no URL was found in extension properties. Add a `zeebe:taskDefinition type="..."` or a property with `name="url"`:

```xml
<extensionElements>
  <zeebe:properties>
    <zeebe:property name="url" value="https://your-api.com/endpoint" />
  </zeebe:properties>
</extensionElements>
```

### Gateway has no choices

Condition expressions on sequence flows weren't parsed. Check that:
1. `<conditionExpression>` elements exist on the outgoing flows
2. The expression uses a supported pattern (see [Condition Expression Mapping](#5-condition-expression-mapping))
3. The flow's `sourceRef` matches the gateway's `id`

### Execution fails with "States.TaskFailed"

The HTTP endpoint in the resource URI is unreachable. The StepFlow interpreter calls real endpoints — you need the target API running, or replace the resource with `internal://echo` for testing.

### Stale `.json` file overrides converted BPMN

If a `.json` file with the same name as a `.bpmn` file exists in `Flows/`, the JSON is loaded first and the BPMN converts to an updated version. Delete the stale JSON or rename it to force fresh conversion.

---

## Quick Reference: Resource Schemes

| Scheme | Handler | Description |
|---|---|---|
| `http://`, `https://` | `InvokeHttpAsync` | HTTP GET/POST/PUT/DELETE with auth support |
| `rule://{id}` | `HandleRuleAsync` | Execute SQL rule via RuleEngine (SQLite) |
| `ai://decision` | `HandleAiAsync` | Call LLM for yes/no decision |
| `flow://{id}` | `HandleFlowAsync` | Invoke another StepFlow state machine |
| `tool://{name}` | `HandleToolAsync` | Call external tool endpoint |
| `internal://echo` | `HandleInternalAsync` | Return input unchanged (testing) |
| `internal://engine/status` | `HandleInternalAsync` | Return rule engine status |
