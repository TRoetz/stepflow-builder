---
name: stepflow
description: Operate the StepFlow durable workflow engine (Amazon States Language dialect) in this repo — create, inspect and run flows through its MCP server or REST API, wire DataExchange data pipelines, add human approval gates, and migrate SSIS / Azure Logic Apps logic. Use whenever asked to build, modify, execute or debug a StepFlow flow, profile, or migration in this codebase.
---

# StepFlow Skill

StepFlow is a **durable workflow engine** (.NET 10 / ASP.NET Core) that executes state machines written in an Amazon States Language (ASL) dialect, plus a React canvas UI for designing them and a **Model Context Protocol (MCP) server** so AI agents can drive the engine directly. Flows are persisted to disk (`flow-state/`) and survive restarts; suspended executions auto-resume on boot.

Deep reference material lives in this repo — read it before non-trivial work:

| Document | Contents |
|---|---|
| `docs/StepFlow_Usage_Guide.md` | Full node catalog (25 types), 10 end-to-end scenarios with complete ASL JSON, DataExchange profile anatomy + example, SSIS & Logic Apps migration procedures |
| `docs/UserManual.md` | Engine internals: state-type semantics, choice-rule operators, payload pipeline order, error handling (`Retry`/`Catch`) |
| `StepFunctionsApp/Converters/README.md` | BPMN/SSIS conversion pipeline (pattern matching → manifest → rollback) |
| `README.md` | Project layout, backend integration overview |

## 1. Hosts & ports — read this first

Two separate hosts exist; flows in the sample scenarios reference **both**:

| Host | Port | What it serves | Start with |
|---|---|---|---|
| Main app (`StepFunctionsApp`) | `http://localhost:5001` (bound per the `DynamicApi` section of appsettings.json; override via `DynamicApi__ListenAddress`) | Flow engine REST API, `/mcp`, human tasks, DataExchange | `dotnet run --project StepFunctionsApp` from the repo root |
| Fake test host (`Stepflow-Builder-Tests`) | `http://localhost:5095` (fixed by its `Program.cs`) | Only the fake commerce/data APIs under `/api/fake/*` used by sample flows and DataExchange fixtures | `dotnet run --project StepFunctionsApp/Stepflow-Builder-Tests` from the repo root |

Health check: `GET http://localhost:5001/api/health` (liveness + flow-state store status).

## 2. MCP server — the primary AI interface

The main app exposes an MCP server at **`http://localhost:5001/mcp`** (tool classes in `Mcp/`: `FlowTools`, `HumanTaskTools`, `FormCaptureTools`, `FormTools`, `DataExchangeTools`, `MetaDataTools`, `EavTools`, `DynamicApiTools`, `SolutionTools`, `RuleTools`; wired in `Program.cs` via `AddMcpServer().WithHttpTransport()` + `MapMcp("/mcp")`). Transport is **streamable HTTP, stateless**: every request is a plain JSON-RPC 2.0 POST with headers `Content-Type: application/json` and `Accept: application/json, text/event-stream`; responses are framed as SSE (`data:` lines). No session handshake needed.

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
| `save_flow` | `name` (string), `statesJson` (JSON **string** of the states object), `startAt?` (defaults to first key), `description?`, `subProjectPath?` (org/project/sub; defaults to the configured default node) | `{ id, name, created, updatedAt }`. **Upsert by name**: saving again with the same name replaces the definition. Persists to the workspace sub-project (§7) so it survives restarts and shows in the UI catalog. Validates that `statesJson` parses, is non-empty, and that `startAt` names an existing state; dangling `next`/choice targets surface as errors at run time |
| `run_flow` | `idOrName` (string), `inputJson?` (JSON object string, default `{}`) | Synchronous execution: `{ executionId, status, output, errorCode, errorMessage, history: [{ type, state, data }] }` |
| `get_execution` | `executionId` (string) | `{ executionId, stateMachineName, status, input, output, currentNode, completedAt, errorCode, errorMessage, history: [{ type, state, data }] }`; works for running/suspended/terminal — terminal records survive restarts. Poll after completing a human task: the resumed run continues in the background (§5) |
| `delete_flow` | `idOrName` (string) | Unregisters from the in-memory registry: `{ deleted: true, id }`. In-flight executions keep their captured definition and continue; stored execution records are untouched — use `delete_execution` for those (§2 caveat) |
| `list_executions` | — | Every stored execution (any status), oldest checkpoint first: `{ executionId, stateMachineName, status, currentState, startedAt, checkpointedAt }`; summaries survive restarts. Use `get_execution` for the full record of one |
| `stop_execution` | `id` (execution id or flow id/name) | Cancels in-flight runs under that id; they finish as Aborted and are persisted so recovery won't resurrect them: `{ message, stopped }` (`stopped` = number canceled) |
| `resume_execution` | `id` (execution id) | Re-registers the flow and queues a stored Suspended execution for background resume: `{ message, status }`. Errors when not found / not resumable / already running or terminal; poll `get_execution` after |
| `delete_execution` | `id` (execution id) | Removes the stored checkpoint (cancels first if still running): `{ message }`; gone from `list_executions`/`get_execution` afterwards. Use to clean up terminal executions |
| `list_human_tasks` | `status?` (`'Pending'\|'Completed'`), `executionId?` | Array of `{ taskId, status, executionId, stateMachineName, stateName, nextState, isEnd, title, assignee, completionType, createdAtUtc, completedAtUtc }` for tasks suspended at a HumanTask/FormCapture state (§5) |
| `get_human_task` | `taskId` (string) | Full record + payload snapshot captured at suspension (`payload.input`, `payload.task`) and the completion result once done. For form-bound tasks use `get_form_capture` to see the bound form + attribute contract |
| `complete_human_task` | `taskId` (string), `resultJson?` (JSON value string, default `{}`) | Completes a pending task — resumes or terminates its execution: `{ taskId, status, executionId, nextState, isEnd }`. Without a state ResultPath the result replaces the flow input; with one it merges at that path. The resumed run continues in the background — poll `get_execution` (§5) |
| `submit_form_capture` | `taskId` (string), `valuesJson` (JSON object string `{ "<attributeName>": value }`) | Form-capture completion: validates/coerces values against the bound attribute domain, persists an EAV row (bound forms only), then resumes. Call `get_form_capture` first for expected attributes; structured failures pass through as-is (`{error}` / `{errors}`) (§5/§8) |
| `get_form_capture` | `taskId` (string) | Task metadata + full form definition (fields, layout) + the bound domain's attributes — everything needed to build a valid submission (§5/§8) |
| `list_form_definitions` | — | Every saved version of every form: `{ formId, version, isCurrentVersion, title, attributeDomainName }`. Use `get_form_definition` for the full page definition |
| `get_form_definition` | `formId` (string), `version?` (exact label; defaults to current) | Full form JSON including the Page UIPage (`RootElements`) and bound domain/schema names; error when unknown |
| `save_form_definition` | `definitionJson` (JSON **string** of the full FormDefinition: `formId`, `version?` default `"1"`, `isCurrentVersion?` default true, `title`, `page` with `RootElements`; optional `attributeDomainName` to bind) | Upsert by formId+version; returns `{ status: "saved", formId, version }`. Binding a saved AttributeDomain is what makes a new HumanTask flow with completion type `"form"` work headlessly — submissions validate against the domain and persist EAV rows (§5) |
| `delete_form_definition` | `formId` (string) | Deletes every saved version of the form: `{ status: "deleted", formId }`; error when unknown |
| `list_data_exchange_profiles` | — | Array of `{ id, name, subProjectPath, stageCount }` for every profile on disk (§6) |
| `get_data_exchange_profile` | `idOrName` (string) | Full profile JSON in camelCase; fetch before modifying an existing profile |
| `save_data_exchange_profile` | `profileJson` (JSON **string** of the full profile), `subProjectPath?` (e.g. `'org/project/sub'`) | Upsert by the name inside the JSON; returns `{ id }`. Omitting `subProjectPath` leaves it unassigned at the workspace root |
| `delete_data_exchange_profile` | `idOrName` (string) | `{ deleted: true\|false }` |
| `run_data_exchange_profile` | `idOrName` (string), `inputJson?` (JSON object string with inline rows, e.g. `'{"rows":[{...}]}'`) | Synchronous execution result (`success`, `rowsIn`, `rowsOut`, …); omitting input uses the profile's configured data source |
| `list_attribute_domains` | — | Array of `{ name, version, description, attributeCount, schemaDefinition }` for every entity contract |
| `get_attribute_domain` | `name` (string) | Full domain JSON in camelCase including attributes and linked schema definition |
| `save_attribute_domain` | `domainJson` (JSON **string** of the full domain), `schemaName?`, `schemaVersion?` (defaults to latest saved version of `schemaName`) | Upsert by the name inside the JSON; returns `{ name }`. Links the domain to a saved schema definition |
| `delete_attribute_domain` | `name` (string) | `{ deleted: true\|false }` |
| `list_schema_definitions` | — | One row per saved version: `{ name, version, description }` |
| `get_schema_definition` | `name` (string), `version?` (omit for the latest saved version) | Full schema JSON in camelCase including its Definition body |
| `save_schema_definition` | `schemaJson` (JSON **string** of the full definition, name + version inside) | Upsert by name+version; returns `{ name, version }` |
| `delete_schema_definition` | `name` (string), `version` (string — both required) | Deletes one saved version: `{ deleted: true\|false }` |
| `list_eav_domains` | — | Array of domains with row counts and flags for registry entity contract / attribute domain |
| `read_eav_rows` | `domain` (string), `limit?` (default 100) | Rows in append order: `{ rowKeyId, entityId, entityType, sourceTaskId, capturedAtUtc, values }` — the flow-side read of persisted EAV data (§4 scheme 14) |
| `write_eav_row` | `domain`, `valuesJson` (JSON object string), `entityId?`, `entityType?`, `sourceTaskId?` | Appends a row; returns `{ rowKeyId }` |
| `update_eav_row` | `domain`, `rowKeyId`, `valuesJson` | Whole-object replacement of the row's values: `{ updated: true\|false }` |
| `patch_eav_row` | `domain`, `rowKeyId`, `patchJson` (only the keys to add/overwrite) | Partial merge into an existing row: `{ patched: true\|false }` |
| `delete_eav_row` | `domain`, `rowKeyId` | `{ deleted: true\|false }` |
| `list_eav_entities` | — | Every registry entity contract with full attribute definitions (`attributeName`, `dataType`, `isRequired`, `defaultValue`, `jsonPathMapping`) |
| `register_eav_entity` | `entityJson` (JSON **string** of the full entity contract) | Upsert by name inside the JSON; returns `{ name }` |
| `delete_eav_entity` | `name` (string) | Removes an entity contract from the registry: `{ deleted: true\|false }` |
| `list_dynamic_apis` | `nodePathPrefix?` (e.g. `'org/project'`) | Array of `{ id, name, nodePath, basePath, handler summary, published/active flags }`; omit prefix for all APIs |
| `get_dynamic_api` | `id` (string) | Full definition JSON in camelCase including all operations; fetch before modifying an existing API |
| `save_dynamic_api` | `definitionJson` (JSON **string** of the full definition, name inside) | Upsert by name inside the JSON; returns `{ id, created }` |
| `delete_dynamic_api` | `id` (string) | `{ deleted: true\|false }` |
| `list_rules` | — | Every persisted named rule: `{ name, kind, description, updatedAtUtc }`. Kinds: `choice`, `jsonata`, `sql`, `ms-rules`, `ai-decision` (§12) |
| `get_rule` | `name` (string) | Full rule including its `definition` body; 404-style error when unknown |
| `save_rule` | `ruleJson` (JSON **string** of `{ name, kind, description?, definition }`) | Upsert by name. Engine-backed kinds register immediately: `sql` → `RuleEngineService` (`rule://<name>`), `ms-rules` → Microsoft RulesEngine (`rules://<name>`) |
| `delete_rule` | `name` (string) | Removes the rule and unregisters it from its engine: `{ deleted: true\|false }` |
| `export_solution` | `nodePath?`, `seedTables?` (comma-separated table names), `seedDomains?` (comma-separated EAV domains), `name?`, `version?` | One portable JSON package for a workspace node — flows (+ canvas layouts), forms + attribute domains, dynamic APIs, data-exchange profiles, named rules (persisted + logic extracted from the flows), EAV datasets, SQL schemas (§12) |
| `import_solution` | `packageJson` (JSON **string** of the export output), `targetNodePath?` | Applies a package to this instance — idempotent redeploy: upserts by identity, appends only new EAV rows, runs datasource migrations. Returns an apply report (§12) |

Conventions that matter when calling these tools:

- **Errors are JSON, not exceptions**: every tool returns `{ "error": "…" }` on failure (unknown flow, bad JSON, unknown `startAt`). Read the message and fix the input.
- All ten state types (§3) — including `HumanTask` and `FormCapture` — work in MCP-saved definitions. A `run_flow` that hits a pending one returns **status `Suspended`**; complete it headlessly with `complete_human_task` (plain tasks) or `submit_form_capture` (form-bound), then poll `get_execution` until terminal (§5). The REST routes and file drop work in parallel, and the engine auto-resumes suspended executions on app restart.
- Definitions round-trip with the React UI: camelCase keys, dictionary keys (state names) keep their original casing.
- **MCP `save_flow` persists** to the workspace sub-project (§7) — it survives restarts and shows in the UI catalog. The raw REST `POST /api/flows` registers into memory only and vanishes on restart; seeded `Flows/*.json` load at startup, and workspace sub-project saves persist to disk under `<sub>/flows/{flowId}/`.

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

Ten types (`StateType` enum, `StepFlow.Asl/StatesLanguageModels.cs`). Common optional fields on every state: `comment`, `inputPath`, `outputPath`, `resultPath`, `parameters`, `resultSelector`, `retry[]`, `catch[]`.

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

`CompositeResourceInvoker.InvokeAsync` (`StepFlow.Asl/ResourceInvoker.cs`) dispatches on the `resource` URI scheme — the complete set of fourteen:

| # | Scheme | URI form | Behavior |
|---|---|---|---|
| 1–2 | `http://`, `https://` | any URL | HTTP call; input object is the body (POST) or query params (GET); supports `auth` config (`Bearer`/`OAuth` token) |
| 3 | `rule://` | `rule://<ruleId>?eav=<entity>` | SQL-expression rule execution (SQLite-backed); optional EAV mapping of dynamic JSON to a strict entity dictionary. Rule ids are looked up case-insensitively (`Uri.Host` lowercases). Persisted named rules live in `rules.json` and auto-register at startup (§12) |
| 4 | `rules://` | `rules://<workflowName>` | Microsoft RulesEngine workflow |
| 5 | `transform://` | `transform://<operation>` | DuckDB transform (or script) over the input — batch SQL, joins, aggregations |
| 6 | `ai://` | any path (ignored) | Single-shot question to a local LLM via the app's own `/api/ai/ask`. The state input is POSTed verbatim; recognized fields: `prompt?`, `messages?` (`[{role, content}]`) — at least one required — plus optional overrides `provider?`, `baseUrl?`, `model?`, `maxTokens?`, `temperature?`. Unset fields fall back to the appsettings `Ai:` section (`Provider`, `BaseUrl`, `Model`; default `lmStudio` @ `http://localhost:1234`); an empty model auto-detects the first id from `{baseUrl}/v1/models` (Ollama: `/api/tags`). Success → response `{ answer }` becomes the state output. **Every failure mode returns HTTP 200 + `{ isError: true, errorMessage }`** (validation error, unreachable LLM, upstream non-2xx, missing `choices[0].message.content`) — the engine then throws `States.TaskFailed: AI failed: <errorMessage>`. The callback base is appsettings `Callback:BaseUrl`, or `http://localhost:{DynamicApi:ManagementPort}` when unset (default 5001 — this app itself) |
| 7 | `flow://` | `flow://<flowId>` | Synchronous sub-flow execution; a failed child throws its error code (or `SubFlow.Failed`) |
| 8 | `tool://` | `tool://<toolName>` | POSTs the input to `{callbackBaseUrl}/api/tools/{toolName}/execute`; same callback-base derivation as `ai://` (`Callback:BaseUrl`, else this app's management port) |
| 9 | `internal://` | `internal://echo`, `internal://engine/status`, `internal://rules/status`, `internal://transform/status` | Built-in diagnostics; any other path throws `States.TaskFailed: Unknown internal resource`. **Use `internal://echo` for smoke tests** — it returns a deep clone of the input with no external dependencies |
| 10 | `dataexchange://` | `dataexchange://<profileId>` | Runs a DataExchange profile pipeline end-to-end (§6) |
| 11 | `ssh://` | `ssh://<hostName>` | Curated host inventory (`ssh_hosts.json`) + AI safety check on the command (`"override": true` in input bypasses it); output `{ host, command, exitCode, stdout, stderr, durationMs }`; `timeoutSeconds` default 30 (values below 1 are reset to 30) |
| 12 | `fetch://` | `fetch://<hostName>?proto=scp\|sftp\|ftp\|xcopy` | Remote file fetch with wildcards (SCP: none); input `sourcePath`, `destDir`, `timeoutSeconds` (default 120) |
| 13 | `sql://` | `sql://<connectionString>` | Local SQLite file only (absolute/CWD-relative path, `file:` URI, or `Data Source=<path>`); remote connection strings fail with an explicit error. **Logical names**: if the argument is not an existing file it is looked up in appsettings `SqlDataSources` (`"fees": "fees.db"`) — env vars `%VAR%`/`${VAR}` expand, so each environment binds its own path while flows stay portable (see §12). Input `{ query }` required; SELECT/WITH → `{ rows: [...], count }`, any other statement → `{ changes: n }`. No `@param` binding or `{{node.field}}` interpolation (UI-only) — wire dynamic values upstream via JSONata/`.$` |
| 14 | `eav://` | `eav://<entityType>` | CRUD on the file-based EAV row store (`eav-data/{domain}.json`). Input `{ operation?, values?, rowKeyId? }`: `read` (default) → bare JArray of rows in append order; `write` (explicit `values`, or upstream output via `"values.$": "$"`) → `{ count: n }`; `update`/`patch` (need `rowKeyId`) → `{ updated\|patched: true }`; `delete` → `{ removed: true }` |

## 5. Human tasks

A `HumanTask` state suspends the execution and creates a task record (8-char id). Completion arrives via MCP or out-of-band channels:

- **MCP** (headless): `complete_human_task(taskId, resultJson?)` for plain tasks; `submit_form_capture(taskId, valuesJson)` for form-bound ones — it validates/coerces values against the bound attribute domain and persists an EAV row before resuming. The resumed run continues in the **background**: poll `get_execution(executionId)` until terminal (§2).
- **API**: `POST http://localhost:5001/api/human-tasks/{taskId}/complete` with the human's result JSON — resumes or terminates the execution.
- **File drop**: write `{taskId}.json` into the watched directory (`Completion.directory`, default `human-task-completions/`).
- **FormCapture states** (REST): complete via `POST /api/form-captures/{taskId}/submit` with the filled form JSON — validates/coerces values against the bound attribute contract, persists an EAV row, then resumes (§8).

The completion result is placed at the state's `resultPath` in the flow input (or replaces the entire input when unset). Discovery: MCP `list_human_tasks(status?, executionId?)` / `get_human_task(taskId)`; REST `GET /api/human-tasks?status=&executionId=`, `GET /api/human-tasks/{id}`.

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
| DELETE | `/api/flows/{id}` (alias `/api/state-machines/{id}`) | Unregister a flow from the in-memory registry; 404 when unknown. In-flight executions keep their captured definition and continue running; durable recovery re-registers the flow while execution records exist (§2 caveat) |
| POST | `/api/flows/executions/{id}/stop` (alias `/api/state-machines/{id}/stop`) | Stop a running execution by id; it reaches Aborted ("Stopped by user") |
| GET | `/api/human-tasks`, `GET /api/human-tasks/{id}`, `POST /api/human-tasks/{id}/complete` | Human task discovery & completion (§5) |
| GET | `/api/form-captures/{taskId}` | FormCapture definition + bound attribute contract for a suspended form task (§5) |
| POST | `/api/form-captures/{taskId}/submit` | Submit the filled form — validates/coerces values, persists an EAV row, resumes the execution (§5) |
| GET/POST/DELETE | `/api/forms`, `GET /api/forms/{formId}?version=`, `DELETE /api/forms/{formId}`, `GET /api/forms/provider` | Saved form definitions: list every version / get one (current, or pinned via `?version=`; 404 when unknown) / create-or-replace (body = full FormDefinition JSON incl. Page) / delete all versions of a form; provider reports the active store (`json`\|`sqlite`). The store behind FormCapture's bound forms and the MCP form tools (§2/§5) |
| POST/GET | `/api/ai/chat`, `POST /api/ai/ask`, `GET /api/ai/models` | Local LLM proxy: chat passthrough for the UI (verbatim OpenAI-compatible body), single-shot question for engine-facing `ai://` states (§4 scheme 6 — failures are HTTP 200 + `{ isError, errorMessage }`), model catalog. Per-request `provider`/`baseUrl` must be local targets; `/api/ai/ask` falls back to the appsettings `Ai:` defaults |
| GET/POST/DELETE | `/api/data-exchange/profiles…`, `POST /api/data-exchange/execute`, `GET /api/data-exchange/executions…` | DataExchange profiles & executions (§6) |
| GET | `/api/workspace` | Workspace tree (org → project → sub-project) with per-node ACLs, flow refs & profile ids (§7) |
| POST/DELETE | `/api/workspace/nodes`, `POST /api/workspace/nodes/rename` | Create / rename / delete org, project or sub-project nodes (§7) |
| GET/PUT | `/api/workspace/access?path=…` | Read local + effective grants / save a node's ACL (§7) |
| GET/POST/DELETE | `/api/workspace/subprojects/{org}/{project}/{sub}/flows[/{flowId}]` | List / save / get / delete flows under a sub-project (§7) |
| GET | `/api/rules`, `GET /api/rules/{*name}`, `POST /api/rules`, `DELETE /api/rules/{*name}` | Named rule catalog (persisted in `rules.json`; engine-backed kinds register on save — §12). `{*name}` is a catch-all: rule names may contain `/` |
| GET | `/api/ssh/hosts` | Curated SSH hosts (name/host/port only — never credentials) |
| GET | `/api/health` | Liveness + flow-state store status |

## 9. Debugging & error handling

- **`run_flow` history** is your first stop: `history[]` lists every state transition with its data; `status` ∈ Running/Succeeded/Failed/Suspended; on failure read `errorCode` + `errorMessage`.
- **Async executions**: poll `GET /api/flows/executions/{id}`; suspended ones resume via the human-task routes or app restart (`FlowState.AutoResume: true`).
- **MCP tools never throw** — errors come back as `{ "error": "…" }`; act on the message.
- Task-level resilience: `retry` (exponential backoff) and `catch` (§3.3); built-in error codes in UserManual §7.
- Engine status endpoints for diagnostics: `internal://engine/status`, `internal://rules/status`, `internal://transform/status`.

## 10. Migration into StepFlow

- **SSIS packages**: the repo ships a converter pipeline (`StepFunctionsApp/Converters/`) — BPMN/SSIS → pattern match against `WorkflowPatterns.json` → conversion manifest with per-step decisions and rollback info. Procedure + concept-mapping table + walkthrough of a real converted flow: **StepFlow_Usage_Guide.md §6**.
- **Azure Logic Apps**: manual porting — map each action to the nearest StepFlow construct (HTTP → `http(s)://` Task, conditions → `Choice`, loops → `Map`, delays → `Wait`, approvals → `HumanTask`). Procedure + worked example: **StepFlow_Usage_Guide.md §7**.

## 11. Working patterns for agents

1. **Smoke-test a new flow** with an `internal://echo` Task before wiring real resources — no external dependencies needed.
2. **Prefer MCP over raw REST** when both are available: smaller payloads, structured errors, same engine.
3. **Save flows by stable name**; re-saving replaces the definition (upsert). Keep a copy of the previous `get_flow` output before replacing anything in production use.
4. **Scenario flows that call `/api/fake/*`** require the fake test host on port 5095 to be running alongside the main app (§1).
5. **Never put credentials in flow JSON.** SSH hosts come from the curated `ssh_hosts.json` inventory; HTTP auth tokens are configured per-resource, not embedded in state names or comments.

## 12. Solution packaging (export/import)

Deploy a whole solution — flows (+ canvas layout) + forms + attribute domains + schema definitions + dynamic APIs + named rules + EAV datasets + SQLite migrations — between environments as one portable JSON package (`"format": "stepflow-solution"`, v2; v1 packages still import).

- **Export**: MCP `export_solution(nodePath?, seedTables?, seedDomains?, name?, version?)` or `GET /api/solutions/export?nodePath=...&seedTables=a,b&seedDomains=x,y`. Collects every flow in the node (including its designer `canvas` layout), the FormCapture forms they reference (with their attribute domains + pinned schema definitions), all dynamic APIs and data-exchange profiles filed under the node, and — for each distinct `sql://<name>` used by those flows — the live database's DDL (`CREATE TABLE`/`INDEX`, idempotent) plus optional seed-table row dumps as `INSERT OR IGNORE`. It also collects: **rules** (the persisted named-rule catalog from `rules.json`, plus decision logic lifted out of the node's flows — `Choice` states → `choice`, `transform://jsonata` tasks → `jsonata`, `ai://` tasks → `ai-decision`; names are `<flow>/<state>`) and **EAV datasets** (entity contracts + row dumps for every domain referenced by an `eav://<domain>` resource or a `rule://…?eav=<domain>` query, plus any explicitly seeded domains). The package is a single JSON file — zip it (with docs/test data if you like) and ship it; import reads the JSON inside.
- **Import**: MCP `import_solution(packageJson, targetNodePath?)` or `POST /api/solutions/import`. Upserts flows (by name, canvas included), schema definitions, domains, forms and APIs in dependency order, upserts named rules into the target's catalog (engine-backed kinds register immediately — `rule://`/`rules://` work right away), registers EAV entity contracts, appends EAV rows **skipping any `rowKeyId` already present**, then runs the datasource migrations against the path bound for that logical name. Re-import is idempotent.
- **Environment binding**: each environment's appsettings maps logical names to local paths — `"SqlDataSources": { "fees": "C:/prod/data/fees.db" }` (env-var expansion supported). Flows reference `sql://fees`, never a machine-specific path. Literal file paths in flows still work and take precedence when the file exists.
- **Test→prod procedure**: export from test → review/diff the package JSON → set prod's `SqlDataSources` binding → import on prod → re-run the smoke suite against prod URLs as the acceptance gate (see `smoke/prod_deploy_check.py`).
- Limits: node-level flow selection only (all flows in the node are exported); same-named flows in different nodes collide (flow names are global); API bearer tokens travel inside the package — treat packages as sensitive. EAV rows are append-only on import (no update/delete of existing target rows).

## 13. Component packages (NuGet)

The backend is split into seven packable class libraries so components can be reused in other apps (`dotnet pack <proj> -o nupkgs`):

| Package | Contents |
|---|---|
| `StepFlow.Asl` | ASL interpreter + checkpointing, resource invoker (all 14 schemes), named-rule catalog + rule engines, human tasks, flow state store (SQLite/Redis), BPMN conversion. References every other package. |
| `StepFlow.DataExchange` | Profile pipeline engine: profile store + file-watch reload, SQL Server/CSV/HTTP sources, DuckDB transforms, routing, execution log. → Metadata + Transform |
| `StepFlow.DynamicApi.Core` | Dynamic API route matching, request/response models, EAV GET mapping. Reference host: `DynamicApiHost`. In-app hosting (`StepFunctionsApp/DynamicApi/`) stays in the builder. |
| `StepFlow.Eav` | EAV registry, JSON row store (idempotent appends), attribute-domain bridging, shared eav GET query language. → Metadata |
| `StepFlow.Forms` | Versioned form definitions (JSON + SQLite stores) + transport-agnostic validation/coercion engine (`FormValidationService`). → Metadata |
| `StepFlow.Metadata` | Attribute domain + schema definition contracts with JSON/SQLite persistence; shared infra types (`StepEngineException`, `KeyPreservingCamelCaseContractResolver`). Leaf. |
| `StepFlow.Transform` | DuckDB transform service shared by ASL and DataExchange. Leaf. |

Notes for code work:
- Types keep their original namespaces (`StepFunctionsApp.StepFunctions`, `StepFlow.DataModel.*`) — the split moved files, not usings; find a type by name, not by folder.
- The builder host (`StepFunctionsApp/Program.cs`) is the canonical DI wiring example for consuming any of these packages elsewhere.
- `InternalsVisibleTo("StepFunctionsApp.Tests")` is set on every package so the test suite can reach internals across assemblies.
