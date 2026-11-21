# StepFlow Builder — Features & Capabilities

What this solution is, what it can replace or stand in for, and how to use every part of it.

---

## 1. What is StepFlow Builder?

StepFlow Builder is a **self-hosted workflow platform** delivered as one repository:

- A **durable execution engine** (.NET 10 / ASP.NET Core) that runs state machines written in an Amazon States Language (ASL) dialect. Flows are checkpointed to disk and survive restarts; suspended executions auto-resume on boot.
- A **visual designer** (React + React Flow canvas) for building those flows with drag-and-drop nodes, live validation, undo/redo, auto-layout and AI assistance.
- A **REST API and an MCP server** so scripts, other applications and AI agents can create, run, inspect and complete workflows without touching the UI.
- A **data layer**: file-based EAV attribute store, versioned entity contracts (attribute domains + schema definitions), validated forms, and staged data-exchange pipelines.
- A **named rules catalog**, **dynamic API designer**, **workspace with per-node access control**, and **solution packaging** for moving an entire environment's worth of work between machines as one JSON file.

Everything runs locally with a single command: `dotnet run --project StepFunctionsApp` serves the engine, REST API, MCP server and (optionally) the built UI on one port (`http://localhost:5001`).

### What it can replace or stand in for

| Instead of | How StepFlow covers it |
|---|---|
| **AWS Step Functions / Azure Logic Apps** (cloud orchestration) | Same ASL dialect, self-hosted: HTTP calls, branching, loops, waits, parallel branches, sub-flows — no cloud dependency, no per-execution billing. Definitions round-trip with the visual canvas. |
| **SSIS packages** | A converter pipeline (`StepFunctionsApp/Converters/`) pattern-matches BPMN/SSIS input into StepFlow flows with a reviewable manifest and rollback info; `sql://` + DuckDB transforms cover ETL-style work. |
| **Hand-rolled glue scripts / cron jobs** | DataExchange profiles: declarative pipelines (source → validation → calculation → lookup → transform → dispatch) runnable from flows, REST, or by simply dropping a file into an inbox folder. |
| **Email + spreadsheet approval workflows** | `HumanTask` states suspend the flow until a person completes it — via the in-app task inbox, a REST call, a dropped JSON file, or an AI agent over MCP. Every completion is recorded in durable execution history. |
| **Simple form backends / intake forms** | Versioned form definitions with a visual builder (or AI-built), rendered and submitted through a standalone page; submissions are validated against the bound entity contract and persisted as EAV rows automatically. |
| **A lightweight attribute store for captured data** | EAV domains (`eav-data/{domain}.json`) with a real query language — equality filters, sorting, paging, field projection — browsable and editable from the UI or REST/MCP. |
| **Decision logic scattered through application code** | Named rules catalog across five kinds (SQL expressions, JSONata, Microsoft RulesEngine workflows, choice tables, AI decisions), callable from any flow via `rule://` / `rules://`. |
| **Internal admin screens for browsing captured rows** | The EAV Data panel: domain list with row counts and contract badges, filterable/sortable/paginated rows table, inline edit/add/delete. |
| **Manual environment-to-environment deployment of workflow solutions** | Solution packages: one portable JSON file containing flows + canvas layouts, forms, entity contracts, dynamic APIs, data-exchange profiles, rules, EAV datasets and SQL schema migrations — imported idempotently on the target. |

Honest scope notes: it is a single-host .NET application (flow-state store supports SQLite or Redis); `sql://` targets local SQLite files only (remote DBs are reached via HTTP APIs instead); the UI panels are operator tooling, while the form-capture page is shareable with end users.

---

## 2. Architecture at a glance

| Host | Port | What it serves | Start with |
|---|---|---|---|
| Main app (`StepFunctionsApp`) | `http://localhost:5001` | Flow engine REST API, MCP server (`/mcp`), human tasks, forms, DataExchange, dynamic APIs; serves the built UI at `/` when `dist/` is copied next to it | `dotnet run --project StepFunctionsApp` |
| Fake test host (`Stepflow-Builder-Tests`) | `http://localhost:5095` | Only the fake commerce/data APIs under `/api/fake/*` used by sample flows and DataExchange fixtures | `dotnet run --project StepFunctionsApp/Stepflow-Builder-Tests` |
| Dynamic API host (`DynamicApiHost`) | `http://localhost:5002` | Standalone host exposing *published* dynamic APIs outside the builder | `dotnet run --project DynamicApiHost` |
| UI dev server (Vite) | `http://localhost:3001` | Designer with HMR; proxies `/api` to :5001 | `cd StepFlow-UI && npm run dev` |

One-command development launcher: `.\start.ps1` from the repo root (backend + fake test host + frontend). A Docker Compose showcase lives in `docker-example/`. Health check: `GET http://localhost:5001/api/health`.

---

## 3. The flow engine

### 3.1 Flow definition format

A flow is a JSON document — the same file the canvas exports, MCP saves and REST registers:

```json
{
  "startAt": "FirstState",
  "states": { "<stateName>": { "type": "...", ... } },
  "version": "1.0",
  "timeoutSeconds": null,
  "queryLanguage": "JSONPath"
}
```

Keys are case-insensitive (the guide standardizes on PascalCase; MCP emits camelCase — both run identically). `queryLanguage` is `"JSONPath"` (default) or `"JSONata"`.

### 3.2 The ten state types

| Type | Key fields | Behavior |
|---|---|---|
| `Task` | `resource` (URI, §3.3), `timeoutSeconds?`, `heartbeatSeconds?` | Invokes a resource; the result becomes the next state's input |
| `Pass` | `result?` | Forwards input unchanged or emits static data — named checkpoint for routing/debugging |
| `Choice` | `choices[]`, `default?` | Evaluates rules top-to-bottom, first match wins. Operators include string/numeric/timestamp comparisons, `isPresent`, `isNull`, type checks, regex `stringMatches`, plus `and`/`or`/`not` combinators. No match + no default → error |
| `Wait` | `seconds?`, `timestamp?`, `secondsPath?`, `timestampPath?` | Pauses execution for a duration — durable across restarts |
| `HumanTask` | `task` (`title`, `assignee`), `completion` (`api` or file drop), `resultPath?` | Suspends until a person completes it (§6) |
| `FormCapture` | `task`, `completion: { "type": "form" }` | Suspends until a JSON-configured form is filled and submitted; submission validates against the bound attribute contract (§6) |
| `Succeed` / `Fail` | `result?` / `error`, `cause` | Terminate successfully or with an error code/message |
| `Parallel` | `branches[]` (sub-definitions) | Runs branches concurrently; all must complete before `next` |
| `Map` | `itemsPath`, `iterator` (per-item sub-flow), `maxConcurrency?` | Iterates an array running the iterator sub-flow per element |

Every state also supports: `comment`, `inputPath`, `outputPath`, `resultPath`, `parameters`, `resultSelector`, and — on Task states — `retry[]` / `catch[]`.

### 3.3 The fourteen resource schemes (Task states)

| Scheme | URI form | What it does |
|---|---|---|
| `http://`, `https://` | any URL | HTTP call; input object is the body (POST) or query params (GET); Bearer/OAuth auth config supported |
| `rule://` | `rule://<ruleId>?eav=<entity>` | SQL-expression rule execution (SQLite-backed), optional EAV mapping of dynamic JSON to a strict entity dictionary |
| `rules://` | `rules://<workflowName>` | Microsoft RulesEngine workflow |
| `transform://` | `transform://<operation>` | DuckDB transform over the input — batch SQL, joins, aggregations (or script) |
| `ai://` | any path | Single-shot question to a local LLM via `/api/ai/ask`; accepts `prompt` or `messages`, optional provider/model overrides; failures return `{ isError: true, errorMessage }` instead of throwing |
| `flow://` | `flow://<flowId>` | Synchronous sub-flow execution |
| `tool://` | `tool://<toolName>` | POSTs the input to a registered tool endpoint on the management port |
| `internal://` | `internal://echo`, `…/engine/status`, … | Built-in diagnostics; **use `internal://echo` for dependency-free smoke tests** |
| `dataexchange://` | `dataexchange://<profileId>` | Runs a DataExchange profile pipeline end-to-end (§7.4) |
| `ssh://` | `ssh://<hostName>` | Curated host inventory + AI safety check on the command; returns `{ host, command, exitCode, stdout, stderr, durationMs }` |
| `fetch://` | `fetch://<host>?proto=scp\|sftp\|ftp\|xcopy` | Remote file fetch with wildcards (SCP/SFTP/FTP) or local copy (xcopy) |
| `sql://` | `sql://<connectionString-or-logical-name>` | Local SQLite only; logical names bind per-environment via appsettings `SqlDataSources` (env-var expansion supported), so flows stay portable. SELECT → `{ rows, count }`, other statements → `{ changes }` |
| `eav://` | `eav://<entityType>` | CRUD on the EAV row store: `read` / `write` / `update` / `patch` / `delete` (§7.1) |

### 3.4 Payload shaping pipeline

Every state shapes its payload in this fixed order: **`inputPath` → `parameters` → *(invoke)* → `resultSelector` → `outputPath`**.

- In JSONPath mode, template properties use a `.$` suffix on the name with a path as value — `"orderId.$": "$.order.id"` resolves against the state input; strings starting with `$$` resolve against the execution context (`$$.Execution.StartTime`, `$$.State.Name`, …); anything else stays literal.
- With `"queryLanguage": "JSONata"`, the whole template object is evaluated as one JSONata expression instead — no suffix needed.

### 3.5 Resilience: retry & catch (Task states)

```json
"retry": [{ "errorEquals": ["States.TaskFailed"], "intervalSeconds": 1, "maxAttempts": 3, "backoffRate": 2.0 }],
"catch": [{ "errorEquals": ["States.TaskFailed"], "next": "OnError", "resultPath": "$.error" }]
```

### 3.6 Durability & recovery

- Every state transition is checkpointed (`flow-state/`); terminal records survive restarts.
- Suspended executions (human tasks, waits) **auto-resume on app boot** (`FlowState.AutoResume: true`).
- `stop_execution` cancels in-flight runs; they finish as Aborted and are persisted so recovery won't resurrect them.

### How to use the engine

```bash
# Register + run synchronously (curl, no MCP client needed)
curl -s http://localhost:5001/api/flows/execute-sync/<flowId> \
  -H 'Content-Type: application/json' -d '{"input": {"orderId": "42"}}'

# Run async, then poll
curl -s -X POST http://localhost:5001/api/flows/execute/<flowId> -H 'Content-Type: application/json' -d '{}'
curl -s http://localhost:5001/api/flows/executions/<executionId>

# Or drive everything through MCP (see §12) — run_flow, get_execution, list_executions, …
```

---

## 4. The visual designer

The React canvas (`StepFlow-UI`) is a chaiNNer-style node editor built on xyflow (React Flow v12) + Zustand:

- **Node palette** (left): steps organized in **13 categories** — Terminal, Flow, AI, Rule, Data, API, Transform, Utility, SubFlow, Human Task, Form Capture, Remote, Transfer — with search and per-node favorites. Click to add at canvas center or drag to a position.
- **Custom node components per category**: AI nodes look different from SQL nodes; each renders its own config summary.
- **Property panel** (right): schema-driven dynamic forms — text/number/dropdown/toggle/slider/color/file/api-selector fields plus Monaco code editors for SQL, JSONata and prompts. Three tabs: **Config**, **Info**, **Template** (save the current configuration as a reusable template). Changes apply immediately; no Save button needed per node.
- **Real-time validation**: green/red borders per node with rule details in the panel; overall validity in the status bar.
- **Undo/redo** (50-entry history), **ELKJS auto-layout**, minimap, zoom controls.
- **Execution visualization**: click **Run** and watch animated progress move through nodes as they execute — either a local browser simulation (**Local** mode) or against the real .NET engine with durable state (**Live Backend** mode).
- **Flow catalog & persistence**: Save stores the flow (localStorage + backend workspace), Load Project lists saved flows, and any flow exports as plain ASL JSON.
- **AI assistance in-canvas**: an agent chat panel for asking questions about/edits to your flow, plus a canvas assistant; AI builders exist for forms (§7.3) and dynamic APIs (§10).

Keyboard shortcuts: `Ctrl+Z` undo · `Ctrl+Y` redo · `Ctrl+S` save · `Ctrl+Enter` run/stop · `Ctrl+Shift+F` auto-layout · `Delete` remove selection · `Escape` deselect.

The full node catalog (25 types with fields) and ten end-to-end worked scenarios live in **`docs/StepFlow_Usage_Guide.md`**; engine internals (state semantics, choice operators, error codes) in **`docs/UserManual.md`**.

---

## 5. Management panels (header toolbar)

Beyond the canvas, the header exposes operator panels — each backed by a dedicated service and REST endpoint:

| Panel | What it does | How to use it |
|---|---|---|
| **Workspace** | Browse/manage the org → project → sub-project tree; per-node ACLs (viewer/editor/admin/owner); select which sub-project is the save target for flows/profiles | Create nodes, edit `access.json` grants, pick a sub-project before saving |
| **Dynamic API** | Design REST APIs as JSON documents: node tree + API list per workspace node, full CRUD, publish flags; includes an AI build wizard that drafts definitions from a description | Pick a node → add/edit an API definition → save (upsert by name) → call it in-app or via the standalone host (§10) |
| **Data Exchange** | Profile wizard, schema editor and pipeline visualizer for DataExchange profiles; AI build modal | Build source → stages → dispatch visually, run inline, inspect per-stage results (§7.4) |
| **Forms** | Visual form builder window (drag fields, layout), form renderer preview, AI form builder; versioned definitions with optional binding to an attribute domain | Design a form → bind it to an entity contract → reference it from a `FormCapture` state (§6/§7.3) |
| **Human Tasks** | Inbox of pending/completed tasks (polls every 5 s): title, flow, assignee, elapsed time; detail view with payload JSON; for form-bound tasks opens the standalone capture page; otherwise a Monaco editor pre-filled `{}` + **Complete** button | Review a suspended task → submit its result → the execution resumes in the background (§6) |
| **Executions** | Backend execution history: status badges (Running/Suspended/Succeeded/Failed/Stopped), current node, timestamps; detail view with input/output/error JSON and full state-transition timeline; **Resume** / **Stop** / **Delete** actions | Click a row to inspect any stored run — including ones that survived restarts (§3.6) |
| **Rules** | Named rule catalog CRUD: list with kind badges, editor for name/kind/description + Monaco definition with per-kind starter templates; save registers engine-backed kinds immediately | Author a `sql` or `ms-rules` rule → call it from flows via `rule://<name>` / `rules://<name>` (§8) |
| **EAV Data** | Domain list with row counts and contract badges (registry entity / attribute domain); rows table with dynamic value columns, filter input (`key=value,key2=value2`), sort, pagination; per-row edit/delete + add-row form | Browse captured data from flows/forms → fix or extend values inline (§7.1) |
| **Solution Package** | Export/import dialog: export a workspace node as one portable JSON (with optional seed tables/domains); import a package with an apply report per section | Move work between environments — see §11 |

The header also carries the theme toggle (dark/light, persisted across reloads), help modal and status bar.

---

## 6. Human-in-the-loop workflows

### 6.1 Plain human tasks (`HumanTask` state)

```json
"Approve": {
  "Type": "HumanTask",
  "Task": { "title": "Approve refund for order $.orderId", "assignee": "finance" },
  "Completion": { "Type": "api" },
  "ResultPath": "$.approval",
  "Next": "AfterApproval"
}
```

The execution suspends and a task record (8-char id) is created. Completion arrives through **any** of four parallel channels:

1. **UI inbox** — Human Tasks panel → select task → submit result JSON (Monaco editor).
2. **REST** — `POST /api/human-tasks/{taskId}/complete` with the result body.
3. **File drop** — write `{taskId}.json` into the watched directory (`Completion.directory`, default `human-task-completions/`).
4. **MCP / AI agent** — `complete_human_task(taskId, resultJson?)`.

The completion result is placed at the state's `resultPath` (or replaces the whole input when unset), and the resumed run continues in the background — poll `get_execution` until terminal. Discovery: `GET /api/human-tasks?status=&executionId=` or MCP `list_human_tasks`.

### 6.2 Form capture (`FormCapture` state)

For structured intake instead of free-form JSON: bind a saved form definition to an **attribute domain** (entity contract), reference it in the state, and completion goes through `POST /api/form-captures/{taskId}/submit` (or MCP `submit_form_capture`, or the standalone page `/form-capture.html?taskId=…` that the UI inbox opens). The submission is **validated and coerced against the bound attribute contract** — structured failures come back as `{error}`/`{errors}` — then a row is persisted to the domain's EAV store before the execution resumes. `get_form_capture` returns everything needed to build a valid submission (form fields, layout, expected attributes).

---

## 7. The data layer

### 7.1 EAV attribute store

Captured data lands as flat key/value rows in per-domain files (`eav-data/{domain}.json`). Rows: `{ rowKeyId, entityId?, entityType?, sourceTaskId?, capturedAtUtc, values }`.

- **Write paths**: `eav://` states in flows, form-capture submissions, REST/MCP write tools, the EAV Data panel.
- **Query language** (shared by REST + MCP): any non-reserved query key is an equality filter (OR within a key, AND across keys); reserved: `sort=capturedAtUtc|-entityId`, `page` / `limit` (default 100) / `offset`, `fields`.
- **REST**: `GET/POST /api/eav/{domain}/rows`, `PUT/PATCH/DELETE /api/eav/{domain}/rows/{rowKeyId}`, plus `GET /api/eav/domains` for the domain list with row counts and contract flags.

### 7.2 Attribute domains & schema definitions (entity contracts)

Versioned contracts describing an entity's attributes (`attributeName`, `dataType`, `isRequired`, `defaultValue`, `jsonPathMapping`) linked to a saved schema definition version. They power form validation/coercion, EAV row semantics and the registry-entity flags shown in the UI. CRUD via REST/MCP; the MCP tools are `list/get/save/delete_attribute_domain` and `…_schema_definition`.

### 7.3 Forms

Versioned form definitions (JSON or SQLite store) with a full Page structure (`RootElements`). Manage them through:

- **UI**: FormBuilderWindow (visual builder), FormRenderer preview, AI form builder (describe → draft).
- **REST/MCP**: `list/get/save/delete_form_definition` — upsert by formId+version; binding an attribute domain is what makes a new `FormCapture` flow work headlessly.

### 7.4 DataExchange profiles

A profile is a reusable, file-based data pipeline: it declares where rows come from (`DataSource`: SQL Server / CSV / HTTP), what happens to them (staged `PipelineStages` of typed actions — validation, calculation, lookup, transformation, dispatch) and optionally how it's triggered. Profiles live under their workspace sub-project and execute three ways:

1. **In a flow** — Task state `"Resource": "dataexchange://<profileId>"`; result reports at least `success`, `rowsIn`, `rowsOut`.
2. **REST** — profile CRUD + `POST /api/data-exchange/execute` (inline rows or the configured source) + execution history endpoints.
3. **File drop** — a monitor polls `<inbox>/<profileId>/` every 5 s; a stable new file executes the profile with `{ "filePath": … }` as input, then moves the source to `processed/` or `failed/`.

Full field-by-field anatomy plus a complete working profile: **`docs/StepFlow_Usage_Guide.md` §4**.

---

## 8. Named rules catalog

Five rule kinds, persisted in `rules.json`, auto-registered at startup:

| Kind | Definition shape (starter) | Engine backing |
|---|---|---|
| `choice` | `{"conditions":[{"expression":"$.x > 1","next":"StateA"}],"defaultNext":"StateB"}` | Evaluated by the ASL interpreter |
| `jsonata` | `{"expression":"$."}` | JSONata engine |
| `sql` | `{"expression":"SELECT … WHERE x = @p1","requiredParameters":["p1"],"isBlocking":true}` | RuleEngineService (SQLite) → callable as `rule://<name>` |
| `ms-rules` | `{"rules":[{"ruleName":"r1","expression":"…"}]}` | Microsoft RulesEngine → callable as `rules://<name>` |
| `ai-decision` | `{"question":"…","systemPrompt":"…","confidenceThreshold":0.7}` | Local LLM via the AI proxy |

Manage from the **Rules panel** (UI), REST (`GET/POST /api/rules`, `DELETE /api/rules/{*name}` — names may contain `/`) or MCP (`list/get/save/delete_rule`). Saving an engine-backed kind registers it immediately, so flows can call it right away. Solution export also lifts decision logic *out of* flows (Choice states → `choice` rules) into the package's rule section (§11).

---

## 9. AI integration

- **Local LLM proxy** — `/api/ai/chat` (OpenAI-compatible passthrough for the UI), `/api/ai/ask` (single-shot, used by `ai://` states; failures are HTTP 200 + `{ isError, errorMessage }`), `/api/ai/models`. Defaults come from appsettings `Ai:` (`Provider`, `BaseUrl`, `Model`; default **lmStudio @ http://localhost:1234**; an empty model auto-detects the first id from `{baseUrl}/v1/models`, Ollama via `/api/tags`). Per-request provider/baseUrl must be local targets.
- **`ai://` states** — any flow can ask a question mid-execution; recognized input fields: `prompt?` / `messages?` plus optional `provider?`, `baseUrl?`, `model?`, `maxTokens?`, `temperature?`.
- **AI nodes in the canvas** (purple category): AI Decision and AI Text Generation steps with schema-driven config.
- **AI builders**: form builder (describe a form → generated definition), dynamic API wizard (describe an endpoint set → drafted API definition).
- **Canvas assistant / agent panel** — chat about your flow, get edits suggested in context.
- **SSH safety check** — `ssh://` commands pass through an AI safety gate before execution (`"override": true` in the input bypasses it deliberately).

---

## 10. Dynamic APIs

Define REST endpoints as JSON documents (operations with request/response shapes, handlers) and file them under workspace nodes:

- **In-app hosting**: published/active APIs are served by the main app itself — external hosts can forward requests unchanged (the EAV rows surface is deliberately wire-compatible).
- **Standalone host**: `DynamicApiHost` on :5002 exposes published APIs outside the builder.
- **EAV-backed GET mapping**: operations can map directly onto EAV domains, turning captured data into a queryable API without writing code.
- Manage via the **Dynamic API panel** (node tree + per-node API list + CRUD), MCP (`list/get/save/delete_dynamic_api`) or REST.

---

## 11. Solution packaging — moving work between environments

Export a whole workspace node as one portable JSON package (`"format": "stepflow-solution"`, v2; v1 still imports) and import it on another machine:

**Included in an export**: every flow in the node (with its designer canvas layout), the FormCapture forms they reference (with attribute domains + pinned schema definitions), all dynamic APIs and data-exchange profiles filed under the node, named rules (persisted catalog **plus** decision logic extracted from the flows — Choice states → `choice`, JSONata transforms → `jsonata`), EAV datasets, and for each distinct `sql://<name>` used: the live database's DDL (`CREATE TABLE`/`INDEX`, idempotent) plus optional seed-table row dumps as `INSERT OR IGNORE`.

**Import is an idempotent redeploy**: upserts flows (canvas included), schema definitions, domains, forms and APIs in dependency order; registers rules (engine-backed kinds work immediately); appends EAV rows **skipping any `rowKeyId` already present**; runs the datasource migrations against the path bound for that logical name.

**Environment binding**: each environment's appsettings maps logical names to local paths — `"SqlDataSources": { "fees": "C:/prod/data/fees.db" }` (env-var expansion supported). Flows reference `sql://fees`, never a machine-specific path; literal file paths still work and take precedence when the file exists.

**Test → prod procedure**: export from test → review/diff the package JSON → set prod's `SqlDataSources` binding → import on prod → re-run the smoke suite against prod URLs as the acceptance gate (`smoke/prod_deploy_check.py`).

Use it via the **Solution Package dialog** (export tab: node path, optional name/version, seed tables/domains → download `<name>-<version>.json`; import tab: pick file + optional target node override → per-section apply report), MCP (`export_solution` / `import_solution`) or REST (`GET /api/solutions/export`, `POST /api/solutions/import`).

Limits to know: node-level flow selection only (all flows in the node); flow names are global, so same-named flows in different nodes collide; API bearer tokens travel inside the package — treat packages as sensitive.

---

## 12. Programmatic access

### 12.1 REST surface (port 5001)

Grouped by area: **flows** (register/list/get/execute sync+async/delete), **executions** (list/get/resume/stop/delete), **human tasks** (list/get/complete), **form captures** (get definition + submit), **forms** (versioned CRUD + provider info), **AI proxy** (chat/ask/models), **data exchange** (profiles, execute, executions), **workspace** (tree, node create/rename/delete, ACLs, sub-project flows), **rules**, **EAV rows & domains**, **dynamic APIs**, **SSH hosts**, **health**. Full route table: `SKILL.md` §8.

### 12.2 MCP server — the primary AI interface

The main app exposes a stateless streamable-HTTP MCP server at **`http://localhost:5001/mcp`** with **51 tools** across ten tool classes (flows, executions, human tasks, form capture, forms, data exchange, metadata/domains/schemas, EAV rows & entities, dynamic APIs, rules, solutions). Every request is a plain JSON-RPC 2.0 POST — no session handshake:

```json
{ "mcpServers": { "stepflow": { "url": "http://localhost:5001/mcp" } } }
```

Conventions that matter when calling tools:

- **Errors are JSON, not exceptions** — every tool returns `{ "error": "…" }` on failure; read the message and fix the input.
- All ten state types work in MCP-saved definitions; a `run_flow` hitting a pending human task returns status `Suspended` — complete it headlessly (`complete_human_task` / `submit_form_capture`) then poll `get_execution`.
- Definitions round-trip with the React UI (camelCase keys, original casing preserved).
- **MCP `save_flow` persists** to the workspace sub-project (survives restarts, shows in the UI catalog); raw REST `POST /api/flows` registers into memory only.

Manual call without an MCP client:

```bash
curl -s http://localhost:5001/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

---

## 13. Migration from existing systems

- **SSIS / BPMN packages**: the repo ships a converter pipeline (`StepFunctionsApp/Converters/`) — input is pattern-matched against `WorkflowPatterns.json`, producing a conversion manifest with per-step decisions and rollback info before anything is applied. Concept-mapping table + walkthrough of a real converted flow: `docs/StepFlow_Usage_Guide.md` §6; pipeline internals in `StepFunctionsApp/Converters/README.md`.
- **Azure Logic Apps**: manual porting — map each action to the nearest StepFlow construct (HTTP → `http(s)://` Task, conditions → `Choice`, loops → `Map`, delays → `Wait`, approvals → `HumanTask`). Procedure + worked example: `docs/StepFlow_Usage_Guide.md` §7.

---

## 14. Running & deploying

| Mode | How |
|---|---|
| **Development** | `.\start.ps1` (backend :5001 + fake host :5095 + Vite dev server :3001 with HMR and `/api` proxy) |
| **Single process** | `npm run build` in `StepFlow-UI`, copy `dist/` next to the backend as `StepFunctionsApp/dist`, then `dotnet run --project StepFunctionsApp` — engine + API + MCP + UI all on :5001 |
| **Docker** | `docker-example/` compose stack: nginx-served frontend + Dynamic API host (:5002) + backend |
| **Standalone dynamic APIs** | `dotnet run --project DynamicApiHost` — published APIs outside the builder |

Sample flows and fixtures live in `workspace-data/Default/Default/Default/flows/`; scenario flows that call `/api/fake/*` need the fake test host on :5095 running alongside.

---

## 15. Reusable components (NuGet)

The backend is split into seven packable class libraries (`dotnet pack <proj> -o nupkgs`) so individual capabilities can be embedded in other applications:

| Package | Contents |
|---|---|
| `StepFlow.Asl` | ASL interpreter + checkpointing, resource invoker (all 14 schemes), named-rule catalog + rule engines, human tasks, flow-state store (SQLite/Redis), BPMN conversion. References every other package |
| `StepFlow.DataExchange` | Profile pipeline engine: profile store + file-watch reload, SQL Server/CSV/HTTP sources, DuckDB transforms, routing, execution log |
| `StepFlow.DynamicApi.Core` | Dynamic API route matching, request/response models, EAV GET mapping (reference host: `DynamicApiHost`) |
| `StepFlow.Eav` | EAV registry, JSON row store (idempotent appends), attribute-domain bridging, shared query language |
| `StepFlow.Forms` | Versioned form definitions + transport-agnostic validation/coercion engine |
| `StepFlow.Metadata` | Attribute domain + schema definition contracts with JSON/SQLite persistence; shared infra types. Leaf package |
| `StepFlow.Transform` | DuckDB transform service shared by ASL and DataExchange. Leaf package |

The builder host (`StepFunctionsApp/Program.cs`) is the canonical DI wiring example for consuming any of these elsewhere.

---

## Where to go deeper

| Document | Contents |
|---|---|
| `docs/StepFlow_Usage_Guide.md` | Full node catalog (25 types), 10 end-to-end scenarios with complete ASL JSON, DataExchange profile anatomy + example, SSIS & Logic Apps migration procedures |
| `docs/UserManual.md` | Engine internals: state-type semantics, choice-rule operators, payload pipeline order, error handling (`Retry`/`Catch`) and built-in error codes |
| `SKILL.md` | Canonical operational reference: hosts/ports, all 51 MCP tools with parameters/returns, REST route table, resource scheme details, solution packaging procedure |
| `StepFunctionsApp/Converters/README.md` | BPMN/SSIS conversion pipeline (pattern matching → manifest → rollback) |
| `README.md` | Project layout, tech stack, getting started, UI walkthrough |
