# Building with Dynamic APIs — Complete Guide

A practical, end-to-end reference for creating, consuming, testing, and deploying **Dynamic APIs** in StepFlow Builder. For the implementation history and design rationale see [`Dynamic-API.md`](Dynamic-API.md); for system context see [`Architecture.md`](Architecture.md).

---

## 1. What a Dynamic API is

A **Dynamic API** is a user-defined REST endpoint attached to a workspace node (org / project / sub-project, depth 1–3) and an attribute domain. Every request under `/api/dynamic/…` that matches one of its operations is:

1. matched against the active APIs (method + full path),
2. checked against the API's **per-API bearer token** (empty token = open access),
3. dispatched to exactly one of four **handlers**:

|Handler|What it does|Required field on the operation|
|---|---|---|
|`flow`|Executes a StepFlow flow synchronously; response is the flow's `Output` verbatim|`flowId`|
|`attributeDomain`|Reads or saves an attribute-domain definition (SQLite or JSON provider)|domain: op-level `domainName` **or** api-level `attributeDomain`|
|`eav`|CRUD on EAV rows captured by flows (`eav-data/{domain}.json`, one JSON array per domain)|same domain rule as above|
|`dataExchange`|Runs a DataExchange profile with the merged request input|`profileId`|

Key properties:

- **Definitions live in SQLite** (`stepflow_data.db`) regardless of `FormData.Provider`; only the attributeDomain *handler* honors the configured provider.
- **Active vs published**: an API is served by the engine as soon as it is saved active (default). Setting `isPublished: true` additionally makes it visible to the external **DynamicApiHost** (§8), which polls every 30 s.
- **Two serving surfaces, one behavior**: the in-process dispatcher (`StepFunctionsApp/DynamicApi/DynamicApiDispatcher.cs`) and the external host share the matching/mapping code in `StepFlow.DynamicApi.Core`, so both behave identically.
- Every request is logged at Information level: method, path, api id, handler, status, elapsed ms.

### Anatomy of a definition (wire shape, camelCase)

```jsonc
{
  "id": "",                    // optional on save; empty = create (or reuse same Name+NodePath)
  "name": "Orders",            // required
  "description": "",           // optional
  "nodePath": "Default",       // required: 'org' | 'org/project' | 'org/project/sub', must exist in the workspace
  "basePath": "/orders",       // required: starts with '/', no spaces; normalized (no '//', no trailing '/')
  "attributeDomain": "OrderApproval",  // api-level default domain for eav/attributeDomain ops
  "bearerToken": "secret-123", // optional; empty/null = open access
  "isActive": true,            // default true — engine serves it
  "isPublished": false,        // default false — host does not pick it up
  "operations": [              // required, non-empty
    {
      "method": "GET",         // GET | POST | PUT | PATCH | DELETE
      "path": "",              // "" or "/…"; "{name}" template segments allowed
      "handlerType": "eav",    // flow | attributeDomain | eav | dataExchange
      "flowId": null,          // handler=flow only (required)
      "domainName": null,      // overrides api-level attributeDomain for this op
      "profileId": null,       // handler=dataExchange only (required)
      "description": ""
    }
  ]
}
```

The **request URL** of an operation is `/api/dynamic` + `joinPaths(basePath, op.path)` — paths are joined with `//` collapsed, a single leading `/`, and no trailing `/` (except bare `/`). Example: basePath `/orders` + op path `/run/{orderId}` → `GET /api/dynamic/orders/run/{orderId}`.

### Save-time validation rules (exact server errors)

|Rule|Error (400 unless noted)|
|---|---|
|`name` missing/blank|`name is required`|
|`nodePath` not 1–3 segments, or bad segment chars|`nodePath must be 'org', 'org/project' or 'org/project/sub'` / `nodePath segment '{seg}' is not a valid workspace node name`|
|Node does not exist in the workspace|**404** `Workspace node '{nodePath}' not found`|
|`basePath` doesn't start with `/` or contains spaces|`basePath must start with '/' and contain no spaces`|
|`operations` missing/empty|`operations must be a non-empty array`|
|Method not in the allowed set|`operations[i].method must be one of GET, POST, PUT, PATCH, DELETE`|
|Op path neither `""` nor `/…`|`operations[i].path must be empty or start with '/'`|
|Full path is `/apis` or under it (reserved management prefix)|`basePath cannot use the reserved '/apis' management prefix`|
|Template segment not matching `^[A-Za-z_][A-Za-z0-9_]*$`|`operations[i].path template parameter '{seg}' must match ^[A-Za-z_][A-Za-z0-9_]*$`|
|Unknown handler type|`operations[i].handlerType must be one of flow, attributeDomain, eav, dataExchange`|
|`flow` without `flowId` / `dataExchange` without `profileId`|`operations[i].flowId is required for handlerType 'flow'` / `…profileId is required for handlerType 'dataExchange'`|
|`eav`/`attributeDomain` with no domain at op or api level|`operations[i] needs a domain: set operations[i].domainName or the api-level attributeDomain`|
|`eav` GET with more than one `{param}` in the full path|`operations[i].path supports at most one {param} for eav GET operations`|
|Same method+full-path twice **inside** this API|`Route conflict: {M} {path} is defined more than once in this API`|
|Same method+full-path already used by another active API|**409** `Route conflict: {M} {path} already defined by api '{id}'`|

Save semantics: `POST /api/dynamic/apis` **creates or updates**. An explicit `id` targets that row; otherwise an existing API with the same `Name` + `NodePath` (case-insensitive name) reuses its id. Response: `{ "id": …, "created": true }` with **201** on create, **200** on update.

---

## 2. Running the stack

Start everything from the repo root:

```powershell
.\start.ps1
```

|Process|Port|Purpose|
|---|---|---|
|.NET Execution Engine (`StepFunctionsApp`)|**5001** (management)|Builder UI at `/`, all controllers, MCP, dynamic APIs of **every** node. Also serves `dist/index.html` in single-process mode.|
|Node endpoint(s) from `DynamicApi.Endpoints[]`|e.g. **5101**|Scoped surface for one workspace subtree (§9). Shipped sample: `{ "Port": 5101, "NodePath": "Acme UI/Website" }`.|
|Vite dev server (`StepFlow-UI`)|**3001**|Dev UI; proxies `/api/*` → `localhost:5001`.|
|Fake Test API Host|**5095**|Target of the sample flows in `Flows/FakeData_*.json`; used by DataExchange profiles.|
|DynamicApiHost (start separately)|**5002**||

```powershell
dotnet run --project DynamicApiHost   # external published-API host, polls engine every 30 s
```

`DynamicApiHost/appsettings.json`: `Urls: http://localhost:5002`, `Engine.BaseUrl: http://localhost:5001`, `Engine.TimeoutSeconds: 120`, `Sync.IntervalSeconds: 30`.

---

## 3. Building an API in the panel (manual)

1. Open the builder UI (`http://localhost:3001` in dev, or `http://localhost:5001/` single-process).
2. Click the **Globe** button in the app header → the **Dynamic API panel** opens.
3. **Left column — node tree**: pick any workspace node (depth 1–3). The APIs attached to that node are listed under it; click one to load it into the editor, or create a new one.
4. **Right column — editor draft**:
   - *Shell*: name, description, basePath (`/…`), api-level attribute domain (pre-filled from the node's first domain when available), bearer token with generate/copy buttons, active/published checkboxes.
   - *Operations table*: add rows; per row set method, op path (may contain `{param}` segments), handler type, and the handler-specific field (`flowId` / `domainName` / `profileId`). The selects are populated from live lookups: attribute domains, workspace flows, DataExchange profiles.
5. **Save** — goes through `POST /api/dynamic/apis`; server validation errors (§1) are shown verbatim. A 409 route conflict names the conflicting API id.
6. **Bottom tabs**:
   - *OpenAPI* — renders the generated spec for `/api/dynamic/openapi.json` (all active APIs), with a copy button.
   - *Test* — pick an operation, fill path params / query / JSON body, send against the engine; status + response body are shown. 401s render correctly when the token is wrong or missing.

---

## 4. Building an API with the AI Wizard

The panel header has a **Sparkles (AI Wizard)** button. It opens a four-phase guided flow (`StepFlow-UI/src/components/DynamicApi/AiDynamicApiWizard.tsx`) that uses your saved local-model config (no backend LLM proxy):

1. **Shell** — name, node (pre-filled from the panel selection), basePath, api-level domain (pre-filled from the node's first domain), bearer token (generate/copy).
2. **Operations** — for each operation you pick a REST method and write a one-line intent ("list all captured order rows"). The prompt is built by `StepFlow-UI/src/services/aiDynamicApiBuilder.ts` from **live context**: attribute domains + their attributes, up to 3 sample EAV rows per domain, workspace flows, DataExchange profiles. The model returns ONE operation draft (method/path/handler/domain), which is validated client-side against the exact rules in §1 and shown as an **always-editable** form — AI output never bypasses validation.
3. **Review & Save** — full definition table; save via the standard endpoint; server errors (e.g. cross-API 409) appear verbatim with a regenerate shortcut.
4. **Test & Deploy** — test any operation against the in-process engine (same-origin `/api/dynamic/…`) or a user-supplied DynamicApiHost base URL (cross-origin; the host is CORS-enabled for exactly this), plus publish + curl-command copy for UAT/Docker targets.

For smoke runs without a real model, `smoke/mock-lmstudio.cjs` returns a canned eav op-draft response (method taken from the user message).

---

## 5. Consuming your APIs — REST contracts

### Auth and input merge

- Send exactly `Authorization: Bearer <token>` (single space) when the API has a token; missing/wrong → **401** `{ "error": "Invalid or missing bearer token" }` + header `WWW-Authenticate: Bearer realm="stepflow-dynamic"`.
- For `flow` and `dataExchange` handlers, request input is merged with precedence **path params > query string > JSON body**.

### eav handler

Effective domain = op `domainName`, falling back to the api-level `attributeDomain`; if neither is set → 500 `Operation has no attribute domain configured`.

**GET — three modes**, decided by the op path template:

|Mode|Op path shape|Behavior|
|---|---|---|
|Collection|no `{param}` (e.g. `` or `/orders`)|Full query language applied; **200** `{ "rows": […], "count": N }` where `count` is the post-filter total, not the page size.|
|Lookup|one `{id}` on an empty or one-segment path (e.g. `/{orderId}`)|Single-row lookup: exact `entityId` match first, falling back to `rowKeyId`. **404** `Row '{id}' not found`; exactly one match → the row object; several → a JSON array. Only the `fields` query param is honored in this mode.|
|EntityFilter|one `{id}` on a multi-segment path (e.g. `/{orderId}/comments`)|Collection filtered by `entityId = {value}`, full query language applied. A client-sent `entityId` equal to the param value is deduped; a different one → **400** `Conflicting entityId: …`.|

**Query language** (collection + entityFilter modes):

|Param|Meaning|
|---|---|
|any other key, e.g. `customerName=Acme`|Equality filter on that row field; repeated keys are OR'd (`?priority=High&priority=Low`).|
|`sort=capturedAtUtc,-entityId`|Comma-separated sort fields; `-` prefix = descending.|
|`page=2`|1-based page number. Mutually exclusive with `offset` → **400** `Use either 'page' or 'offset', not both`.|
|`limit=50`|Page size (default 100).|
|`offset=20`|Skip N rows.|
|`fields=a,b,c`|Project only these fields on each row.|

Unparseable reserved values fall back to defaults leniently.

**POST** — body properties `entityId` / `entityType` (case-insensitive) become the row's identity fields; every other property is captured into the row's `values`. A missing `rowKeyId` is auto-assigned. **201** with the full stored row object.

**PUT / PATCH** — require a `{rowKeyId}` path parameter (missing → 400 `This operation requires a {rowKeyId} path parameter`). PUT replaces the row's values, PATCH merges them; unknown key → **404** `Row '{id}' not found`; success → **200** with the updated row.

**DELETE** — same `{rowKeyId}` requirement; **200** `{ "status": "deleted", "rowKeyId": … }` or 404 as above. Other methods on an eav op → 501.

### attributeDomain handler

- **GET** → **200** with the full domain definition (same wire shape as `GET /api/attribute-domains` items), or **404** `Attribute domain '{name}' not found`.
- **POST / PUT / PATCH** — body is an attribute-domain entry; an empty name in the body adopts the operation's target domain, a *different* name → **400** `Body domain name does not match operation target`. Saved through the configured provider (sqlite or json) → **200** `{ "status": "saved", "domainName": … }`.
- **DELETE** → 501.

### flow handler

Input is merged (§5), then the flow runs synchronously:

|Flow result|HTTP|Body|
|---|---|---|
|Succeeded|200|the flow's `Output` verbatim|
|Suspended (e.g. human task)|**202**|`{ "status": "suspended", "executionId": … }` — resume later via the normal flow APIs|
|Failed / Aborted / TimedOut|500|`{ "error": <errorCode>, "message": … }`|

Unknown `flowId` (neither in-memory registry nor workspace sub-project flow file) → **404** `Flow '{id}' not found`. No auto-creation of flows.

### dataExchange handler

Runs the profile with merged input; **200** with the profile result verbatim (`{}` when empty). Unknown profile → **404** `Profile '{id}' not found`; execution failure → 500 with the message.

### Error surface summary

|Status|When|
|---|---|
|400|Bad JSON body, query-language conflict, missing `{rowKeyId}`, save-validation failures (§1)|
|401|Missing/wrong bearer token (with `WWW-Authenticate` header)|
|404|No API matches the method+path; unknown flow/profile/domain/row|
|405|Path exists under another method of some active API — response carries an `Allow` header listing them|
|409|Cross-API route conflict at save time|
|500|Handler failure, no domain configured, unexpected exception (`{ "error": … }`)|
|501|Method not supported by the handler (e.g. DELETE on attributeDomain)|

---

## 6. OpenAPI spec

`GET /api/dynamic/openapi.json` returns an OpenAPI 3 document covering **all active APIs** — paths, parameters (including eav query params), `securitySchemes.bearerAuth`, and per-domain schema components. On a scoped node port (§9) the same URL serves only that subtree's APIs. The panel's OpenAPI tab renders it live; use it to hand consumers an interrogable contract.

---

## 7. Testing from the UI or curl (engine)

Panel Test tab: pick op → fill params/body → send → inspect status + body against `http://localhost:5001/api/dynamic/…`.

curl equivalents:

```bash
# collection with filter + sort + paging
curl -H "Authorization: Bearer secret-123" \
  "http://localhost:5001/api/dynamic/orders?customerName=Acme&sort=-capturedAtUtc&page=1&limit=50"

# single-row lookup (entityId, falls back to rowKeyId)
curl -H "Authorization: Bearer secret-123" http://localhost:5001/api/dynamic/orders/ORD-999

# append a row -> 201 with the stored row (rowKeyId auto-assigned)
curl -X POST -H "Authorization: Bearer secret-123" -H "Content-Type: application/json" \
  -d '{"orderId":"ORD-999","customerName":"E2E"}' http://localhost:5001/api/dynamic/orders

# patch / delete by rowKeyId (op path must contain {rowKeyId})
curl -X PATCH -H "Authorization: Bearer secret-123" -d '{"notes":"ready"}' \
  http://localhost:5001/api/dynamic/orders/rows/<rowKeyId>
```

---

## 8. Publishing to the DynamicApiHost (UAT / Docker)

The host (`DynamicApiHost`, port **5002**) is an external twin of the in-process dispatcher for published APIs:

- Set `isPublished: true` on the API (panel checkbox or save payload). The host polls the engine's catalog every **30 s** and loads new/changed definitions; verify with `GET http://localhost:5002/health` → `{ "status": "ready", "apisLoaded": N }`.
- It executes handlers over HTTP against `Engine.BaseUrl` (default `http://localhost:5001`, 120 s timeout), so the same request/response contracts of §5 apply unchanged.
- **Bearer auth is enforced exactly as on the engine** — publishing never weakens it.
- The host runs a permissive CORS policy (`AllowAnyOrigin` + any header/method) so the wizard's Test & Deploy phase can call it cross-origin from the builder UI; every other surface stays same-origin-only. Covered by two facts in `StepFunctionsApp.Tests/DynamicApiHostTests.cs`.

```bash
# after publishing, wait ≤30 s for the poll, then:
curl -H "Authorization: Bearer secret-123" http://localhost:5002/api/dynamic/orders?limit=5
# CORS preflight check (wizard Phase 4 relies on this):
curl -i -X OPTIONS -H "Origin: http://localhost:3001" \
  -H "Access-Control-Request-Method: GET" http://localhost:5002/api/dynamic/orders | head -5
# -> HTTP/1.1 204 No Content + Access-Control-Allow-Origin: *
```

For Docker/UAT targets, point the wizard's Test & Deploy phase (or your own curl) at the host's public base URL; copy the generated curl commands from Phase 4.

---

## 9. Per-node ports and NGINX multi-port hosting

The engine binds **one Kestrel port per workspace node** in addition to the management port, so an NGINX reverse proxy can route each domain/project to its own dedicated backend endpoint. Scoping is by **arrival TCP port** (`HttpContext.Connection.LocalPort`), not the `Host` header — hitting a node port directly (no proxy) is equally scoped.

Configuration (`DynamicApi` section of appsettings.json or env vars):

|Key|Default|Meaning|
|---|---|---|
|`ManagementPort`|`5001`|Full surface: builder UI at `/`, all controllers, MCP, dynamic APIs of **every** node.|
|`ListenAddress`|`localhost`|Every endpoint binds here. Use `*` for cross-host deployment behind NGINX on another machine.|
|`Endpoints[]`|(empty)|One entry per business unit: `{ "Port": 5101, "NodePath": "Acme UI/Website" }`. The port serves dynamic APIs whose `nodePath` is that node **or nested under it**; everything else 404s.|

Env overrides (double-underscore form): `DynamicApi__ManagementPort=6000`, `DynamicApi__ListenAddress=*`, `DynamicApi__Endpoints__0__Port=5102`, `DynamicApi__Endpoints__0__NodePath="Acme UI/Website"`. Invalid config fails fast at startup (out-of-range port, duplicate port, or malformed NodePath).

What each port serves:

- **Management port** — everything.
- **Node port** — only `/api/dynamic/{…}` for that node's subtree (including its scoped `openapi.json`) plus `GET /api/health`. The builder UI, MCP, and definition CRUD (`/api/dynamic/apis`) are management-only; on a node port they 404.
- **Operator endpoint** — `GET /api/dynamic/endpoints` (management port) returns `{ "managementPort": …, "listenAddress": …, "endpoints": [ { "port", "nodePath" } ] }`; use it to author/verify NGINX upstreams.

NGINX maps each domain → its upstream port (`proxy_pass http://127.0.0.1:5101;`); the app does the rest by arrival port. A ready-made same-host config with a TLS skeleton lives in [`deploy/nginx-stepflow.conf`](../deploy/nginx-stepflow.conf).

---

## 10. Management API reference

|Endpoint|Purpose|
|---|---|
|`GET /api/dynamic/apis?nodePath=&published=`|List active APIs; optional segment-aware node filter and published-state filter.|
|`GET /api/dynamic/apis/{id}`|One definition (active or not, so inactive ones can be re-saved).|
|`POST /api/dynamic/apis`|Create or update (§1 validation); 201 `{ id, created: true }` on create, 200 on update.|
|`DELETE /api/dynamic/apis/{id}`|Delete; 200 `{ deleted: true, id }` or 404.|
|`GET /api/dynamic/openapi.json`|Generated OpenAPI 3 for all active APIs (scoped on node ports).|
|`GET /api/dynamic/endpoints`|Operator view of the port bindings (§9).|

---

## 11. Worked example — end to end

```bash
# 1) Define an Orders API on the Default node: eav collection + lookup + append
curl -X POST -H "Content-Type: application/json" -d '{
  "name": "Orders",
  "nodePath": "Default",
  "basePath": "/orders",
  "bearerToken": "secret-123",
  "attributeDomain": "OrderApproval",
  "operations": [
    { "method": "GET",  "path": "",            "handlerType": "eav" },
    { "method": "GET",  "path": "/{orderId}",  "handlerType": "eav" },
    { "method": "POST", "path": "",            "handlerType": "eav" }
  ]
}' http://localhost:5001/api/dynamic/apis
# -> 201 { "id": "<api-id>", "created": true }

# 2) Consume it (rows captured by flows into eav-data/OrderApproval.json)
curl -H "Authorization: Bearer secret-123" \
  "http://localhost:5001/api/dynamic/orders?sort=-capturedAtUtc&limit=10"
# -> { "rows": [ … ], "count": N }

# 3) Route conflict check — a second API claiming GET /api/dynamic/orders
curl -X POST -H "Content-Type: application/json" -d '{
  "name": "Orders2", "nodePath": "Default", "basePath": "/orders",
  "attributeDomain": "OrderApproval",
  "operations": [ { "method": "GET", "path": "", "handlerType": "eav" } ]
}' http://localhost:5001/api/dynamic/apis
# -> 409 Route conflict: GET /orders already defined by api '<api-id>'

# 4) Publish it for the external host, then test :5002 after ≤30 s
curl -X POST -H "Content-Type: application/json" -d '{ …same payload…, "isPublished": true }' \
  http://localhost:5001/api/dynamic/apis
curl http://localhost:5002/health            # apisLoaded increments
curl -H "Authorization: Bearer secret-123" http://localhost:5002/api/dynamic/orders?limit=5

# 5) Flow handler — register a trivial flow first (POST /api/flows), then:
#    op { "method": "GET", "path": "/run/{orderId}", "handlerType": "flow", "flowId": "<id>" }
curl -H "Authorization: Bearer secret-123" http://localhost:5001/api/dynamic/orders/run/ORD-999
# -> 200 with the flow's Output (or 202 { status, executionId } when it suspends)
```

---

## Quick reference — where things live

|Concern|Location|
|---|---|
|Definition CRUD + validation|`StepFunctionsApp/Controllers/DynamicApisController.cs`|
|Request dispatch + handlers|`StepFunctionsApp/DynamicApi/DynamicApiDispatcher.cs`|
|Shared matching / EAV GET mapping (engine + host)|`StepFlow.DynamicApi.Core/` (`DynamicApiMatcher`, `EavGetMapper`)|
|SQLite definition store|`StepFunctionsApp/DynamicApi/SqliteDynamicApiStore.cs`|
|OpenAPI generation|`StepFunctionsApp/DynamicApi/DynamicApiOpenApiGenerator.cs`|
|Port bindings / hosting options|`StepFunctionsApp/DynamicApi/DynamicApiHostingOptions.cs`, `appsettings.json → DynamicApi`|
|External published-API host|`DynamicApiHost/Program.cs` (CORS, 30 s sync poll)|
|Builder UI panel + AI wizard|`StepFlow-UI/src/components/DynamicApi/` (`DynamicApiPanel.tsx`, `AiDynamicApiWizard.tsx`)|
|UI service client + context helpers|`StepFlow-UI/src/services/dynamicApiService.ts`|
|AI prompt builder / validation|`StepFlow-UI/src/services/aiDynamicApiBuilder.ts`|
|EAV row store (file-based)|`eav-data/{domain}.json`, `StepFunctionsApp/StepFunctions/EavRowStore.cs`|
