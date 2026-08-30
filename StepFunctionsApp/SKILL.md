---
name: stepflow
description: Operate the StepFlow durable workflow engine (Amazon States Language dialect) in this repo — create, inspect and run flows through its MCP server or REST API, wire DataExchange data pipelines, add human approval gates, and migrate SSIS / Azure Logic Apps logic. Use whenever asked to build, modify, execute or debug a StepFlow flow, profile, or migration in this codebase.
---

# StepFlow Skill

StepFlow is a **durable workflow engine** (.NET 10 / ASP.NET Core) that executes state machines written in an Amazon States Language (ASL) dialect, plus a React canvas UI for designing them and a **Model Context Protocol (MCP) server** so AI agents can drive the engine directly. Flows are persisted to disk (`flow-state/`) and survive restarts; suspended executions auto-resume on boot.

Deep reference material lives in this repo — read it before non-trivial work:

| Document | Contents |
|---|---|
| `../docs/StepFlow_Usage_Guide.md` | Full node catalog (25 types), 10 end-to-end scenarios with complete ASL JSON, DataExchange profile anatomy + example, SSIS & Logic Apps migration procedures |
| `../docs/UserManual.md` | Engine internals: state-type semantics, choice-rule operators, payload pipeline order, error handling (`Retry`/`Catch`) |
| `Converters/README.md` | BPMN/SSIS conversion pipeline (pattern matching → manifest → rollback) |
| `README.md` | Project layout, backend integration overview |

## 1. Hosts & ports — read this first

Two separate hosts exist; flows in the sample scenarios reference **both**:

| Host | Port | What it serves | Start with |
|---|---|---|---|
| Main app (`StepFunctionsApp`) | `http://localhost:5001` (fixed by `Program.cs` `UseUrls`; override via `ASPNETCORE_URLS`) | Flow engine REST API, `/mcp`, human tasks, DataExchange | `dotnet run` from the repo root (the csproj is at the top level) |
| Fake test host (`Stepflow-Builder-Tests`) | `http://localhost:5095` (fixed by its `Program.cs`) | Only the fake commerce/data APIs under `/api/fake/*` used by sample flows and DataExchange fixtures | `dotnet run --project Stepflow-Builder-Tests` |

Health check: `GET http://localhost:5001/api/health` (liveness + flow-state store status).

## 2. MCP server — the primary AI interface

The main app exposes an MCP server at **`http://localhost:5001/mcp`** (`Mcp/FlowTools.cs`; wired in `Program.cs` via `AddMcpServer().WithHttpTransport()` + `MapMcp("/mcp")`). Transport is **streamable HTTP, stateless**: every request is a plain JSON-RPC 2.0 POST with headers `Content-Type: application/json` and `Accept: application/json, text/event-stream`; responses are framed as SSE (`data:` lines). No session handshake needed.

Client configuration (Claude Desktop / VS Code Copilot / any MCP client):

```json
{
  "mcpServers": {
    "stepflow": {
      "url": "http://localhost:5001/mcp"
    }
  }
}
```

### Tools

| Tool | Parameters | Returns |
|---|---|---|
| `list_flows` | — | Array of `{ id, name, description, updatedAt }` for every registered flow |
| `get_flow` | `idOrName` (string) | `{ id, name, description, createdAt, updatedAt, definition }`; `definition` is the full camelCase ASL (`startAt`, `states`) — re-importable into the React canvas unchanged |
| `save_flow` | `name` (string), `statesJson` (JSON **string** of the states object), `startAt?` (defaults to first key), `description?` | `{ id, name, updatedAt }`. **Upsert by name**: saving again with the same name replaces the definition. Validates that `statesJson` parses, is non-empty, and that `startAt` names an existing state; dangling `next`/choice targets surface as errors at run time |
| `run_flow` | `idOrName` (string), `inputJson?` (JSON object string, default `{}`) | Synchronous execution: `{ executionId, status, output, errorCode, errorMessage, history: [{ type, state, data }] }` |

Conventions that matter when calling these tools:

- **Errors are JSON, not exceptions**: every tool returns `{ "error": "…" }` on failure (unknown flow, bad JSON, unknown `startAt`). Read the message and fix the input.
- All ten state types (§3) — including `HumanTask` and `FormCapture` — work in MCP-saved definitions. A `run_flow` that hits a pending one returns **status `Suspended`**; complete it via §5 (or the form-capture routes, §8) to resume it (the engine auto-resumes suspended executions on app restart).
- Definitions round-trip with the React UI: camelCase keys, dictionary keys (state names) keep their original casing.

Manual call without an MCP client:

```bash
curl -s http://localhost:5001/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

## 3. Flow definition format (ASL)

A flow is a JSON document:

```json
{
  "startAt": "FirstState",
  "states": { "<stateName>": { "type": "...", ... } },
  "version": "1.0",
  "timeoutSeconds": null,
  "queryLanguage": "JSONPath"
}
```

- `startAt` is required; every non-terminal state needs `"next"` or `"end": true`.
- Keys are case-insensitive (Json.NET binding) — the guide standardizes on **PascalCase** (`"Type"`, `"Resource"`, `"Next"`); MCP emits camelCase. Both run identically.
- `queryLanguage` is `"JSONPath"` (default) or `"JSONata"` (§3.2).

### 3.1 State types and their fields

Ten types (`StateType` enum, `StepFunctions/StatesLanguageModels.cs`). Common optional fields on every state: `comment`, `inputPath`, `outputPath`, `resultPath`, `parameters`, `resultSelector`, `retry[]`, `catch[]`.

| Type | Key fields | Behavior |
|---|---|---|
| `Task` | `resource` (URI, §4), `timeoutSeconds?`, `heartbeatSeconds?` | Invokes the resource; result is a JToken that becomes next state's input |
| `Pass` | `result?` (JToken) | Forwards input unchanged, or emits static `result`; named checkpoint for routing/debugging |
| `Choice` | `choices[]`, `default?` | Evaluates rules top-to-bottom; first match wins. Rule: `{ "variable": "$.x", "<operator>": value, "next": "State" }`. Operators: `stringEquals(Path)`, `stringGreaterThan/LessThan`, `numericEquals/GreaterThan(Equals)/LessThan(Equals)`, `booleanEquals`, `timestampEquals/GreaterThan/LessThan`, `isPresent`, `isNull`, `isString`, `isNumeric`, `isBoolean`, `stringMatches` (regex), plus combinators `and[]`, `or[]`, `not`. No match + no `default` → error `States.NoChoiceMatched` |
| `Wait` | `seconds?`, `timestamp?`, `secondsPath?`, `timestampPath?` | Pauses execution for a duration (durable across restarts) |
| `HumanTask` | `task` (object; `title`, `assignee` read out), `completion` (`{ "type": "api" \| "file", ... }`, default `api`; file mode: `directory?`, `fileName?` default `{taskId}.json`), `resultPath?` | Suspends the flow until a person completes it (§5). Requires `next` or `end: true` |
| `FormCapture` | `task` (object; `title`, `assignee` read out), `completion` (`{ "type": "form" }`; API only — no file drop) | Suspends the flow until a JSON-configured form is filled and submitted via `POST /api/form-captures/{taskId}/submit` (§8); submission validates against the bound attribute contract. Requires `next` or `end: true` |
| `Succeed` | `result?` | Terminates successfully with output data |
| `Fail` | `error`, `cause` | Terminates with an error code/message |
| `Parallel` | `branches[]` (each a sub-definition `{ startAt, states }`) | Runs branches concurrently; all must complete before `next` |
| `Map` | `itemsPath` (`$.array`), `iterator` (sub-definition run per item), `maxConcurrency?` | Iterates an array running the iterator sub-flow per element |

### 3.2 Payload shaping pipeline & templates

Every state shapes its payload in this order: **`inputPath` → `parameters` → *(invoke)* → `resultSelector` → `outputPath`** (UserManual §6).

- Templates apply to `parameters` and `resultSelector`. In JSONPath mode a template property is written with a `.$` suffix on its name and a path as value: `"orderId.$": "$.order.id"` resolves the JSONPath against the state input; values starting with `$$` resolve against the execution context (`$$.Execution.StartTime`, `$$.State.Name`, …); any other string stays literal.
- With `"queryLanguage": "JSONata"` on the definition, the whole template object is evaluated as one JSONata expression instead — no suffix needed.

### 3.3 Retry & Catch (Task states)

```json
"retry": [{ "errorEquals": ["States.TaskFailed"], "intervalSeconds": 1, "maxAttempts": 3, "backoffRate": 2.0 }],
"catch": [{ "errorEquals": ["States.TaskFailed"], "next": "OnError", "resultPath": "$.error" }]
```

Built-in error codes and semantics: UserManual §7.

## 4. Resource schemes (Task states)

`CompositeResourceInvoker.InvokeAsync` (`StepFunctions/ResourceInvoker.cs`) dispatches on the `resource` URI scheme — the complete set of fourteen:

| # | Scheme | URI form | Behavior |
|---|---|---|---|
| 1–2 | `http://`, `https://` | any URL | HTTP call; input object is the body (POST) or query params (GET); supports `auth` config (`Bearer`/`OAuth` token) |
| 3 | `rule://` | `rule://<ruleId>?eav=<entity>` | NRules rule execution; optional EAV mapping of dynamic JSON to a strict entity dictionary |
| 4 | `rules://` | `rules://<workflowName>` | Microsoft RulesEngine workflow |
| 5 | `transform://` | `transform://<operation>` | DuckDB transform (or script) over the input — batch SQL, joins, aggregations |
| 6 | `ai://` | any path (ignored) | POSTs the input to `{callbackBaseUrl}/api/ai/ask`; fails with `States.TaskFailed` if the response has `"isError": true`. **`callbackBaseUrl` is hardcoded to `http://localhost:5000`** — the UI backend's port, not this app's 5001. No `/api/ai/ask` implementation exists in this repo (pre-existing gap) — AI states fail with a connection error until an external LLM service listens there |
| 7 | `flow://` | `flow://<flowId>` | Synchronous sub-flow execution; a failed child throws its error code (or `SubFlow.Failed`) |
| 8 | `tool://` | `tool://<toolName>` | POSTs the input to `{callbackBaseUrl}/api/tools/{toolName}/execute`; same hardcoded `http://localhost:5000` base as `ai://` |
| 9 | `internal://` | `internal://echo`, `internal://engine/status`, `internal://rules/status`, `internal://transform/status` | Built-in diagnostics; any other path throws `States.TaskFailed: Unknown internal resource`. **Use `internal://echo` for smoke tests** — it returns a deep clone of the input with no external dependencies |
| 10 | `dataexchange://` | `dataexchange://<profileId>` | Runs a DataExchange profile pipeline end-to-end (§6) |
| 11 | `ssh://` | `ssh://<hostName>` | Curated host inventory (`ssh_hosts.json`) + AI safety check on the command (`"override": true` in input bypasses it); output `{ host, command, exitCode, stdout, stderr, durationMs }`; `timeoutSeconds` default 30 (values below 1 are reset to 30) |
| 12 | `fetch://` | `fetch://<hostName>?proto=scp\|sftp\|ftp\|xcopy` | Remote file fetch with wildcards (SCP: none); input `sourcePath`, `destDir`, `timeoutSeconds` (default 120) |
| 13 | `sql://` | `sql://<connectionString>` | Local SQLite file only (absolute/CWD-relative path, `file:` URI, or `Data Source=<path>`); remote connection strings fail with an explicit error. Input `{ query }` required; SELECT/WITH → `{ rows: [...], count }`, any other statement → `{ changes: n }`. No `@param` binding or `{{node.field}}` interpolation (UI-only) — wire dynamic values upstream via JSONata/`.$` |
| 14 | `eav://` | `eav://<entityType>` | CRUD on the file-based EAV row store (`eav-data/{domain}.json`). Input `{ operation?, values?, rowKeyId? }`: `read` (default) → bare JArray of rows in append order; `write` (explicit `values`, or upstream output via `"values.$": "$"`) → `{ count: n }`; `update`/`patch` (need `rowKeyId`) → `{ updated|patched: true }`; `delete` → `{ removed: true }` |

## 5. Human tasks

A `HumanTask` state suspends the execution and creates a task record (8-char id). Completion arrives two ways:

- **API**: `POST http://localhost:5001/api/human-tasks/{taskId}/complete` with the human's result JSON — resumes or terminates the execution.
- **File drop**: write `{taskId}.json` into the watched directory (`Completion.directory`, default `human-task-completions/`).
- **FormCapture states**: complete via `POST /api/form-captures/{taskId}/submit` with the filled form JSON — validates/coerces values against the bound attribute contract, persists an EAV row, then resumes (§8).

The completion result is placed at the state's `resultPath` in the flow input (or replaces the entire input when unset). Discovery routes: `GET /api/human-tasks?status=&executionId=`, `GET /api/human-tasks/{id}`.

```json
"Approve": {
  "Type": "HumanTask",
  "Task": { "title": "Approve refund for order $.orderId", "assignee": "finance" },
  "Completion": { "Type": "api" },
  "ResultPath": "$.approval",
  "Next": "AfterApproval"
}
```

## 6. DataExchange profiles

A **profile** is a reusable, file-based data pipeline: it declares where rows come from (`DataSource`), what happens to them (staged `PipelineStages` of typed `Action`s — validation, calculation, lookup, transformation, dispatch), and optionally how it's triggered. Profiles are JSON documents stored under their workspace sub-project (`<sub>/data-exchange/<profileId>/`, §7) and executed three ways:

1. **In a flow**: Task state with `"Resource": "dataexchange://<profileId>"` — runs synchronously; result reports at least `success`, `rowsIn`, `rowsOut`.
2. **REST** (`api/data-exchange/…`): `GET|POST /profiles`, `GET|DELETE /profiles/{id}`, `POST /execute` (body `{ "profileId": "...", "input": { ... } }`), `GET /executions?limit=50`, `GET /executions/{id}`.
3. **File drop**: the file monitor polls `<inbox>/<profileId>/` every 5s; a stable new file executes the profile with `{ "filePath": … }` as input, then moves the source to `processed/` or `failed/`.

Full anatomy (every field of `DataSource`, stages and action types) plus a complete working profile: **StepFlow_Usage_Guide.md §4**. Live examples in `workspace-data/Default/Default/Default/data-exchange/<profileId>/profile.json`.

## 7. Workspace hierarchy (org → project → sub-project)

Flows and DataExchange profiles are organized on disk in a three-level tree — **organization → project → sub-project** — under the workspace root (`Workspace:RootDirectory` in appsettings; default `workspace-data`). Each node may carry its own local ACL file (`access.json`, entries `{ principal, role }`; roles viewer/editor/admin/owner); effective access for a node is the union of grants from itself and all ancestors (nearest ancestor wins per principal). Sub-projects are leaves: flows live in `<sub>/flows/{flowId}/` (`flow.json` = camelCase ASL + `meta.json`) and profiles in `<sub>/data-exchange/<profileId>/profile.json`. The UI's Workspace panel manages the tree, ACLs, and selecting a sub-project as save target (REST routes in §8). A one-time migration moved legacy flat profiles into `Default/Default/Default/data-exchange/` (idempotent); stray profile folders not under a sub-project surface under `unassignedProfiles` in the tree response.

## 8. REST API surface (main app, port 5001)

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/flows` (alias `/api/state-machines`) | Register/save a flow. Body: `{ name?, description?, id?, startAt?, states: {…} }` or `{ …, definition: { startAt, states } }` |
| GET | `/api/flows` | List registered flows |
| GET | `/api/flows/{id}/definition` (alias `/api/state-machines/{id}`) | Get one flow's definition |
| POST | `/api/flows/execute/{id}` (alias `/api/state-machines/{id}/execute`) | Execute asynchronously; returns an execution id |
| POST | `/api/flows/execute-sync/{id}` | Execute synchronously and wait for final output |
| GET | `/api/flows/executions` / `GET /api/flows/executions/{id}` (alias `/api/execution/{id}`) | List stored executions / get one execution's status+output |
| POST | `/api/flows/executions/{id}/resume` | Resume a suspended/recovered execution |
| DELETE | `/api/flows/executions/{id}` | Delete an execution checkpoint |
| POST | `/api/state-machines/{id}/stop` | Stop an execution |
| GET | `/api/human-tasks`, `GET /api/human-tasks/{id}`, `POST /api/human-tasks/{id}/complete` | Human task discovery & completion (§5) |
| GET | `/api/form-captures/{taskId}` | FormCapture definition + bound attribute contract for a suspended form task (§5) |
| POST | `/api/form-captures/{taskId}/submit` | Submit the filled form — validates/coerces values, persists an EAV row, resumes the execution (§5) |
| GET/POST/DELETE | `/api/data-exchange/profiles…`, `POST /api/data-exchange/execute`, `GET /api/data-exchange/executions…` | DataExchange profiles & executions (§6) |
| GET | `/api/workspace` | Workspace tree (org → project → sub-project) with per-node ACLs, flow refs & profile ids (§7) |
| POST/DELETE | `/api/workspace/nodes`, `POST /api/workspace/nodes/rename` | Create / rename / delete org, project or sub-project nodes (§7) |
| GET/PUT | `/api/workspace/access?path=…` | Read local + effective grants / save a node's ACL (§7) |
| GET/POST/DELETE | `/api/workspace/subprojects/{org}/{project}/{sub}/flows[/{flowId}]` | List / save / get / delete flows under a sub-project (§7) |
| GET | `/api/ssh/hosts` | Curated SSH hosts (name/host/port only — never credentials) |
| GET | `/api/health` | Liveness + flow-state store status |

## 9. Debugging & error handling

- **`run_flow` history** is your first stop: `history[]` lists every state transition with its data; `status` ∈ Running/Succeeded/Failed/Suspended; on failure read `errorCode` + `errorMessage`.
- **Async executions**: poll `GET /api/flows/executions/{id}`; suspended ones resume via the human-task routes or app restart (`FlowState.AutoResume: true`).
- **MCP tools never throw** — errors come back as `{ "error": "…" }`; act on the message.
- Task-level resilience: `retry` (exponential backoff) and `catch` (§3.3); built-in error codes in UserManual §7.
- Engine status endpoints for diagnostics: `internal://engine/status`, `internal://rules/status`, `internal://transform/status`.

## 10. Migration into StepFlow

- **SSIS packages**: the repo ships a converter pipeline (`Converters/`) — BPMN/SSIS → pattern match against `WorkflowPatterns.json` → conversion manifest with per-step decisions and rollback info. Procedure + concept-mapping table + walkthrough of a real converted flow: **StepFlow_Usage_Guide.md §6**.
- **Azure Logic Apps**: manual porting — map each action to the nearest StepFlow construct (HTTP → `http(s)://` Task, conditions → `Choice`, loops → `Map`, delays → `Wait`, approvals → `HumanTask`). Procedure + worked example: **StepFlow_Usage_Guide.md §7**.

## 11. Working patterns for agents

1. **Smoke-test a new flow** with an `internal://echo` Task before wiring real resources — no external dependencies needed.
2. **Prefer MCP over raw REST** when both are available: smaller payloads, structured errors, same engine.
3. **Save flows by stable name**; re-saving replaces the definition (upsert). Keep a copy of the previous `get_flow` output before replacing anything in production use.
4. **Scenario flows that call `/api/fake/*`** require the fake test host on port 5095 to be running alongside the main app (§1).
5. **Never put credentials in flow JSON.** SSH hosts come from the curated `ssh_hosts.json` inventory; HTTP auth tokens are configured per-resource, not embedded in state names or comments.
