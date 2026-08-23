# StepFlow Usage Guide — Examples & Migration

A practical reference for building, running, and migrating workflows in **StepFlow**: the durable state-machine engine (`StepFunctions/`), the visual builder (`StepFlow-UI/`), and the data-exchange pipeline subsystem (`DataExchange/`).

This guide is example-driven. For engine internals (checkpointing, storage layout, configuration keys) see [`UserManual.md`](./UserManual.md); for BPMN conversion see [`BPMN_Converter_Guide.md`](./BPMN_Converter_Guide.md).

**Contents**

1. [Architecture Overview](#1-architecture-overview)
2. [Resource Schemes (12)](#2-resource-schemes-12)
3. [Node Reference — 25 Nodes in 12 Categories](#3-node-reference--25-nodes-in-12-categories)
4. [DataExchange Profiles](#4-dataexchange-profiles)
5. [Ten End-to-End Scenarios](#5-ten-end-to-end-scenarios)
6. [Migration from SSIS Packages](#6-migration-from-ssis-packages)
7. [Migration from Azure Logic Apps](#7-migration-from-azure-logic-apps)

---

## 1. Architecture Overview

### 1.1 Flow definition format (ASL)

A flow is a JSON document in an Amazon States Language–derived format (`StateMachineDefinition` in `StepFunctions/StatesLanguageModels.cs`). Register it with `POST /api/flows`:

```json
{
  "Comment": "What this flow does",
  "StartAt": "FirstState",
  "Version": "1.0",
  "TimeoutSeconds": null,
  "QueryLanguage": "JSONPath",
  "States": {
    "FirstState": { "Type": "Task", "Resource": "...", "Next": "SecondState" },
    "SecondState": { "Type": "Succeed", "End": true }
  }
}
```

|Field|Type|Default|Meaning|
|---|---|---|---|
|`Comment`|string||Free-text description of the flow.|
|`StartAt`|string|required|Name of the first state to execute.|
|`States`|object|required|Map of state name → state definition. Names are unique within a flow (and inside each `Parallel` branch / `Map` iterator).|
|`Version`|string|`"1.0"`|Format version marker.|
|`TimeoutSeconds`|int?||Overall execution timeout.|
|`QueryLanguage`|string|`"JSONPath"`|Expression language for paths and templates: `"JSONPath"` or `"JSONata"`.|

### 1.2 The nine state types

The `StateType` enum defines exactly nine state types. Every state definition may carry `Comment`, `Next` (name of the following state) **or** `End: true` (terminal).

|Type|Purpose|Key properties|
|---|---|---|
|`Task`|Invoke a resource and continue with its result.|`Resource`, `Parameters`, `ResultSelector`, `TimeoutSeconds`, `HeartbeatSeconds`, `Retry[]`, `Catch[]`|
|`Pass`|Checkpoint / no-op; passes input through unchanged (optionally narrowed).|`InputPath`, `OutputPath`|
|`Choice`|Route to the first matching rule.|`Choices[]`, `Default`|
|`Wait`|Sleep for a duration or until a timestamp.|`Seconds`, `Timestamp`, `SecondsPath`, `TimestampPath`|
|`HumanTask`|Suspend until a person completes an external action; checkpointed and resumable across restarts.|`Task`, `Completion`, `ResultPath`|
|`Succeed`|Terminate the execution successfully, optionally with a fixed result.|`Result`|
|`Fail`|Terminate the execution as failed.|`Error`, `Cause`|
|`Parallel`|Run independent branch sub-flows concurrently; fail if any branch fails.|`Branches[]` (each is a nested `{StartAt, States}` definition)|
|`Map`|Iterate an array of items through an iterator sub-flow.|`ItemsPath`, `Iterator` (nested definition), `MaxConcurrency`|

Common properties available on all states:

|Property|Applies to|Meaning|
|---|---|---|
|`InputPath`|all|Dotted path narrowing the state's effective input before processing. Omit or use `"$"` for the whole input.|
|`OutputPath`|all|Dotted path selecting which part of the state output becomes the next state's input.|
|`ResultPath`|Task, Parallel, Map, HumanTask|Merge the result into a dotted path of the *input* instead of replacing it. `"$"` replaces the whole input (the default when omitted).|

**Wait states.** Exactly one of the four wait fields should be set:

```json
"SleepFive": { "Type": "Wait", "Seconds": 5, "Next": "AfterSleep" }
"UntilTomorrow": { "Type": "Wait", "Timestamp": "2026-12-31T00:00:00Z", "End": true }
"DynamicDelay": { "Type": "Wait", "SecondsPath": "$.delaySeconds", "Next": "AfterSleep" }
```

`SecondsPath` / `TimestampPath` resolve the value from the state input (JSONPath or JSONata per `QueryLanguage`).

**Choice states.** A `Choice` evaluates its `Choices[]` in order and routes to the first match; if none matches it must have a `Default`:

```json
"RouteByRegion": {
  "Type": "Choice",
  "Comment": "First matching rule wins",
  "Choices": [
    { "Variable": "$.region", "StringEquals": "North", "Next": "HandleNorth" },
    { "Variable": "$.amount", "NumericGreaterThan": 1000, "Next": "Escalate" }
  ],
  "Default": "HandleStandard"
}
```

**Choice operators** (one per rule; `And`/`Or`/`Not` compose rules recursively):

|Operator|Operand type|Notes|
|---|---|---|
|`StringEquals`, `StringEqualsPath`|string / path|Exact string comparison.|
|`StringGreaterThan`, `StringLessThan`|string / number|Lexicographic or numeric depending on operand.|
|`NumericEquals`, `NumericGreaterThan`, `NumericGreaterThanEquals`, `NumericLessThan`, `NumericLessThanEquals`|number||
|`BooleanEquals`|bool||
|`TimestampEquals`, `TimestampGreaterThan`, `TimestampLessThan`|ISO-8601 string||
|`IsPresent`, `IsNull`, `IsString`, `IsNumeric`, `IsBoolean`|bool|Type/presence checks.|
|`StringMatches`|string|Pattern match against the variable value.|
|`And[]`, `Or[]`|ChoiceRule[]|Logical composition of sub-rules.|
|`Not`|ChoiceRule|Negation of a sub-rule.|

**Retry and Catch (Task states).** Both use error-code matching; `"States.ALL"` matches every error.

```json
"CallFlakyApi": {
  "Type": "Task",
  "Resource": "https://api.example.com/orders",
  "Retry": [
    { "ErrorEquals": ["States.TaskTimeout"], "IntervalSeconds": 2, "MaxAttempts": 4, "BackoffRate": 2.0 }
  ],
  "Catch": [
    { "ErrorEquals": ["States.ALL"], "Next": "OnFailure", "ResultPath": "$.lastError" }
  ],
  "Next": "AfterCall"
}
```

- `Retry`: defaults are `IntervalSeconds: 1`, `MaxAttempts: 3`, `BackoffRate: 2.0`; delay for attempt *n* is `IntervalSeconds × BackoffRateⁿ` seconds.
- `Catch`: routes to the catcher's `Next` state with an error object `{ "Error": "<code>", "Cause": "<message>" }`. If `ResultPath` is set, that object is stored at the path inside a clone of the input; otherwise it becomes the state output.

### 1.3 Execution model

- **Input flows forward.** Every state receives the current flow input (optionally narrowed by `InputPath`). The state's output — after `ResultSelector` → `ResultPath` → `OutputPath` processing — becomes the next state's input.
- **Parameters templates** build a Task state's request payload from the input. In JSONPath mode, any property whose name ends in `.$` is an expression:

  ```json
  "Parameters": {
    "orderId.$": "$.order.id",
    "note.$": "States.Format('Order {} placed', '$.order.id')",
    "traceId.$": "$$Execution.Id"
  }
  ```

  - `"$…"` — JSONPath resolved against the state input.
  - `"$$…"` — resolved against the execution context object: `{ Execution: { Id, StartTime }, State: { Name, EnteredTime }, StateMachine: { Id } }`.
  - Anything else is an intrinsic expression; the only implemented intrinsic is `States.Format('format', '$.a', …)` with `{}` placeholders (converted to `{0}`). Unrecognized strings pass through literally.
  - With `"QueryLanguage": "JSONata"`, templates are evaluated as JSONata expressions instead.

- **ResultSelector** post-processes the raw resource result with the same template mechanism before `ResultPath`/`OutputPath` apply.
- **Parallel semantics.** Each branch in `Branches[]` is a full nested state-machine definition (`{ "StartAt": …, "States": { … } }`) executed concurrently against a deep clone of the effective input. If any branch fails, the Parallel state fails with that branch's error code/message. The state output is a JSON array of the branch outputs (index-aligned), then passed through `ResultSelector`/`ResultPath`/`OutputPath`.
- **Map semantics.** `ItemsPath` (default `"$"`) must resolve to a JSON array; each item is deep-cloned and run through the nested `Iterator` definition. The state output is the array of iterator outputs, then post-processed as in Parallel. `MaxConcurrency` is declared on the state (the builder defaults it to 1).
- **Suspension & resume.** A `HumanTask` state suspends the execution: a checkpoint is written to disk and the process may exit or restart without losing progress. Completion arrives via API (`POST /api/human-tasks/{id}/complete`) or file drop (see §3.4); the result is merged at `ResultPath` (or replaces the input when unset) and the flow resumes from `Next`, or terminates if `End: true`. Recovered-but-not-auto-resumed executions can be resumed with `POST /api/flows/executions/{id}/resume`.
- **Execution statuses:** `Running`, `Succeeded`, `Failed`, `Aborted`, `Suspended`, `TimedOut` (new executions start as `Running`).

### 1.4 Casing rules

Top-level state properties (`Type`, `Resource`, `Parameters`, …) are matched case-insensitively by the JSON serializer — `"type": "Task"` and `"Type": "Task"` both work. This guide standardizes on **PascalCase** (matching `UserManual.md`); note that the builder's export and the converted flows under `Flows/` use lowercase, which is equally valid.

One exception: nested keys inside a HumanTask's `Task` and `Completion` objects are read with case-sensitive token accessors and **must** use exactly the casing shown in §3.4 — `task.title`, `task.assignee` (lowercase) and `completion.Type`, `completion.Directory`, `completion.FileName` (PascalCase).

### 1.5 REST API reference

All routes are served by the host application (`Controllers/`).

**Flows & executions** (`FlowsController.cs`)

|Method & route|Purpose|
|---|---|
|`POST /api/flows` (alias `POST /api/state-machines`)|Register/save a flow. Body: `{ "name"?, "description"?, "id"?, "states": { … } }` or `{ "definition": { "startAt", "states", … } }`. Returns `{ id, name, description, createdAt, updatedAt }`.|
|`GET /api/flows` (alias `GET /api/state-machines`)|List registered flows.|
|`GET /api/flows/{id}/definition` (alias `GET /api/state-machines/{id}`)|Get one flow's definition.|
|`POST /api/flows/execute/{id}` (alias `POST /api/state-machines/{id}/execute`)|Execute asynchronously. Body: the initial input JToken. Returns `{ executionId, status, stateMachineId, stateMachineName }`.|
|`POST /api/flows/execute-sync/{id}`|Execute synchronously and return `{ executionId, status, output, errorCode, errorMessage }`.|
|`GET /api/flows/executions/{id}` (alias `GET /api/execution/{id}`)|Get one execution's status/history.|
|`GET /api/flows/executions`|List stored executions (survives restarts; includes terminal history).|
|`POST /api/state-machines/{id}/stop`|Stop an execution.|
|`POST /api/flows/executions/{id}/resume`|Resume a `Suspended` (or recovered) execution.|
|`DELETE /api/flows/executions/{id}`|Delete a stored execution's checkpoint.|
|`POST /api/state-machines/save-project`|Save and compile as a multi-file project structure on local disk.|
|`POST /api/state-machines/load-project`|Load a multi-file project structure from local disk.|

**SSH host inventory** (`FlowsController.cs`)

|Method & route|Purpose|
|---|---|
|`GET /api/ssh/hosts`|List curated SSH hosts (name/host/port only — never credentials). Backs the `ssh://` and `fetch://` schemes.|

**Human tasks** (`HumanTasksController.cs`)

|Method & route|Purpose|
|---|---|
|`GET /api/human-tasks?status=&executionId=`|List human tasks, optionally filtered by status or execution id.|
|`GET /api/human-tasks/{id}`|Get one task with its full payload and (if completed) result.|
|`POST /api/human-tasks/{id}/complete`|Complete a pending task. Body: the human's result JToken. Resumes or terminates the execution.|

**Data exchange** (`DataExchangeController.cs`) — see also §4.5.

|Method & route|Purpose|
|---|---|
|`GET /api/data-exchange/profiles`|List all profiles.|
|`POST /api/data-exchange/profiles`|Save (create or update) a profile from its JSON document.|
|`GET /api/data-exchange/profiles/{id}`|Get one profile by id **or** name.|
|`DELETE /api/data-exchange/profiles/{id}`|Delete a profile by id or name.|
|`POST /api/data-exchange/execute`|Execute a profile synchronously. Body: `{ "profileId": "…", "input": { … } }`.|
|`GET /api/data-exchange/executions?limit=50`|Recent execution artifacts, newest first.|
|`GET /api/data-exchange/executions/{id}`|One execution artifact by id.|

---

## 2. Resource Schemes (12)

A `Task` state's `Resource` is a URI whose scheme selects the handler in `CompositeResourceInvoker.InvokeAsync` (`StepFunctions/ResourceInvoker.cs`). Dispatch is by prefix, in this order — **twelve schemes** in total:

| # |Scheme|Handler|One-line summary|
|---|---|---|---|
|1|`http://`, `https://`|HTTP invoker|Call any HTTP endpoint (structured or legacy form).|
|2|`rule://<ruleId>[?eav=<entity>]`|RuleEngineService|Evaluate a business rule; optional EAV payload mapping.|
|3|`rules://<workflowName>`|Microsoft RulesEngine|Run a named MS-RulesEngine workflow (C# expressions).|
|4|`transform://<operation>`|ScriptExecutionService / DuckDBTransformService|Run a script (`javascript`, `python`, `powershell`, `csharp`, `shell`) or a DuckDB operation.|
|5|`ai://…`|AI service proxy|POST to `{callbackBaseUrl}/api/ai/ask`; fails the state on error responses.|
|6|`flow://<flowId>`|Sub-flow executor|Execute another registered flow synchronously; its output becomes this state's result.|
|7|`tool://<name>`|Tool proxy|POST to `{callbackBaseUrl}/api/tools/{name}/execute`.|
|8|`internal://…`|Built-ins|`echo`, `engine/status`, `rules/status`, `transform/status`; anything else throws `States.TaskFailed`.|
|9|`dataexchange://<profileId>`|DataExchangeExecutor|Run a DataExchange profile pipeline (§4).|
|10|`ssh://<hostName>`|SshCommandService|Execute a command on a curated host (AI safety-checked unless overridden).|
|11|`fetch://<hostName>?proto=<p>`|FetchRemoteFiles|Pull files from a curated host via `scp`, `sftp`, `ftp`, or `xcopy`.|

(`http` and `https` share one handler; counted separately they make twelve scheme prefixes.)

### 2.1 http / https

Two input forms are supported:

**Structured form** — the state input must carry `"__handler": "http"` plus request fields:

```json
"Parameters": {
  "__handler": "http",
  "method": "POST",
  "body": { "orderId.$": "$.order.id" },
  "query": { "verbose": "true" },
  "auth": { "type": "Bearer", "token": "secret-token" }
}
```

- `method` defaults to `POST`; `GET` and `DELETE` send no body.
- `auth.type`: `Bearer` or `OAuth`.
- Non-JSON responses are wrapped as `{ "body": "<text>" }`.

**Legacy form** — without `__handler`, the entire state input is POSTed as a JSON body to the resource URL.

### 2.2 rule:// (RuleEngineService)

```json
"Resource": "rule://CheckCredit?eav=Customer",
"Parameters": { "limit.$": "$.creditLimit", "requested.$": "$.amount" }
```

- The URI host is the rule id (`rule://CheckCredit` → `CheckCredit`).
- With `?eav=<entityName>`, the input is mapped to a strict EAV dictionary via the EAV registry (`eav_registry.json`) before evaluation; without it, the input is converted naively to a string→object dictionary.

### 2.3 rules:// (Microsoft RulesEngine)

`rules://<workflowName>` executes the named workflow registered with Microsoft RulesEngine (C# expression support). The state input is passed as the workflow's data context.

### 2.4 transform:// (scripts & DuckDB)

The URI path selects the operation:

- **Script operations** — `transform://javascript`, `transform://python`, `transform://powershell`, `transform://csharp`, `transform://shell` (case-insensitive). The script text comes from `input["script"]` or `parameters.script`; the data under transformation comes from `input_data` / `parameters.input_data`, falling back to the whole input.
- **DuckDB operations** — any other operation (`query`, `execute`, …) is handed to DuckDB: the state's `Parameters` object becomes the transform request, with `operation` overridden by the URI path when it isn't `query`.

```json
"LoadData": {
  "Type": "Task",
  "Resource": "transform://query",
  "Parameters": {
    "operation": "query",
    "table": "transactions",
    "data": [ { "id": 1, "amount": 150.0 } ]
  },
  "Next": "Process"
}

"RunPython": {
  "Type": "Task",
  "Resource": "transform://python",
  "Parameters": {
    "script": "import json\nprint(json.dumps({'ok': True}))",
    "input_data": { "value.$": "$.amount" }
  },
  "Next": "AfterScript"
}
```

### 2.5 ai://

POSTs the state input to `{callbackBaseUrl}/api/ai/ask`. If the response indicates an error, the state fails with `States.TaskFailed`; otherwise the AI response becomes the state output. The builder's AI nodes (§3.5) target this scheme.

### 2.6 flow:// (sub-flow call)

`flow://<flowId>` executes another **registered** flow synchronously with the current input; the sub-execution's `Output` becomes this state's result. A failed sub-execution fails the state with error code `SubFlow.Failed`. This is the runtime counterpart of the Sub-Flow Call node (§3.10).

### 2.7 tool://

`tool://<name>` POSTs to `{callbackBaseUrl}/api/tools/{name}/execute` — a proxy for externally registered tools.

### 2.8 internal://

Built-in diagnostics and pass-through:

|Resource|Effect|
|---|---|
|`internal://echo`|Returns the input unchanged.|
|`internal://engine/status`|Engine status snapshot.|
|`internal://rules/status`|Rule engine status.|
|`internal://transform/status`|Transform service status.|

Any other `internal://` target throws `States.TaskFailed` ("Unknown internal resource").

### 2.9 dataexchange://

```json
"ImportOrders": {
  "Type": "Task",
  "Resource": "dataexchange://customer-orders-import",
  "Parameters": {},
  "Next": "AfterImport"
}
```

Runs the named DataExchange profile (§4) with the state input (as a JSON object; an empty object when absent). The pipeline result — including `success`, row counts, and per-stage details — becomes the state output.

### 2.10 ssh://

`ssh://<hostName>` where `<hostName>` is a **curated host name** from `ssh_hosts.json` (listable via `GET /api/ssh/hosts`). The command to run comes from the input (`command` field, or passed through from upstream text); an `override: true` flag bypasses the AI safety check on the command; `timeoutSeconds` defaults to 30.

Output shape:

```json
{ "host": "web-01", "command": "uptime", "exitCode": 0, "stdout": "…", "stderr": "", "durationMs": 42 }
```

### 2.11 fetch://

`fetch://<hostName>?proto=<scp|sftp|ftp|xcopy>` pulls files from a curated host into a local directory. Wildcards (`*`, `?`) are supported in the source path where the protocol allows (not SCP). Output shape:

```json
{
  "host": "file-01",
  "protocol": "sftp",
  "sourcePath": "/exports/*.csv",
  "destDir": "C:\\drop\\in",
  "files": [ { "remotePath": "/exports/a.csv", "localPath": "C:\\drop\\in\\a.csv", "sizeBytes": 1234 } ],
  "fileCount": 1,
  "durationMs": 812
}
```
