# Dynamic API Builder — Plan

**How-to guide:** see [`Dynamic-API-Building-Guide.md`](Dynamic-API-Building-Guide.md) for the complete end-to-end builder's reference — manual + AI-wizard creation, REST contracts per handler, testing, publishing to DynamicApiHost, and NGINX multi-port hosting.

## Context

StepFunctionsApp (ASP.NET Core 10, `C:/Source/stepflow-builder/StepFunctionsApp`) needs a **Dynamic API** subsystem: user-defined REST endpoints that are attached to AttributeDomains and workspace nodes (OU = org / Project / Sub-project), stored in SQLite, where each request is dispatched to one of four handlers — execute a FLOW, read/write an AttributeDomain definition (SQLite or JSON provider), read/write EAV rows captured by flows (`eav-data/`), or run a DataExchange profile. The subsystem must be Bearer-token protected per API and expose a generated OpenAPI 3 spec so consumers can interrogate what the APIs allow. A builder UI panel — in the same style as the existing React flow builder (StepFlow-UI) — creates and tests these APIs.

End state: `POST /api/dynamic/apis` defines an API; requests to `/api/dynamic/{basePath}/{opPath}` are auth-checked and executed by the configured handler; `GET /api/dynamic/openapi.json` returns a spec covering all active APIs; the React builder has a "Dynamic API" panel for CRUD + OpenAPI viewing + live testing.

## Hosting behind NGINX (multi-port, per business unit)

The host binds **one Kestrel port per workspace node** in addition to the management port, so an NGINX reverse proxy can route each domain/project to its own dedicated backend endpoint. Scoping key is the **TCP port the connection arrived on** (`HttpContext.Connection.LocalPort`) — not the `Host` header — so hitting a node port directly (no proxy) is equally scoped.

### Configuration (`DynamicApi` section, appsettings.json or env vars)

|Key|Default|Meaning|
|---|---|---|
|`ManagementPort`|`5001`|Full management surface: builder UI at `/`, all controllers, MCP, and dynamic APIs of **every** node.|
|`ListenAddress`|`localhost`|Every endpoint binds here. Use `*` (or an IP literal) for cross-host deployment behind NGINX on another machine.|
|`Endpoints[]`|`(empty)`|One entry per business unit / project: `{ "Port": 5101, "NodePath": "Acme UI/Website" }`. The port serves dynamic APIs whose `nodePath` is that node **or nested under it**; everything else on the port 404s.|

Sample (shipped in appsettings.json):

```json
"DynamicApi": {
  "ManagementPort": 5001,
  "ListenAddress": "localhost",
  "Endpoints": [ { "Port": 5101, "NodePath": "Acme UI/Website" } ]
}
```

Environment overrides (double-underscore form): `DynamicApi__ManagementPort=6000`, `DynamicApi__ListenAddress=*`, `DynamicApi__Endpoints__0__Port=5102`, `DynamicApi__Endpoints__0__NodePath="Acme UI/Website"`. Invalid config fails fast at startup with a clear message (out-of-range port, duplicate port, or NodePath not shaped `org` / `org/project` / `org/project/sub`).

### What each port serves

- **Management port** — unchanged from before: everything.
- **Node port** — only `/api/dynamic/{…}` for APIs in that node's subtree (including its scoped OpenAPI spec at `/api/dynamic/openapi.json`) plus `GET /api/health`. The builder UI, MCP, and API definition CRUD (`/api/dynamic/apis`) are management-only; on a node port they 404.
- **Operator endpoint** — `GET /api/dynamic/endpoints` (management port) returns `{ "managementPort": …, "listenAddress": …, "endpoints": [ { "port", "nodePath" } ] }`; use it to author/verify NGINX upstreams.

### Routing model

NGINX maps each domain → its upstream port (`proxy_pass http://127.0.0.1:5101;`); the app does the rest by arrival port. A ready-made same-host config (with TLS skeleton) lives in [`deploy/nginx-stepflow.conf`](deploy/nginx-stepflow.conf).


## Approach

All new backend code lives in a new root folder `DynamicApi/` (mirrors `DataExchange/`). All JSON over the wire is camelCase via the existing `KeyPreservingCamelCaseContractResolver`. Error bodies follow the existing convention: `{ "error": "message" }`.

### Step 1 — Models + SQLite store (`DynamicApi/DynamicApiModels.cs`, `DynamicApi/IDynamicApiStore.cs`, `DynamicApi/SqliteDynamicApiStore.cs`)

No equivalent exists; create new.

```csharp
namespace StepFunctionsApp.DynamicApi;

public sealed class DynamicApiOperation
{
    public string Method { get; set; } = "GET";        // GET|POST|PUT|PATCH|DELETE (validated)
    public string Path { get; set; } = "";             // "" or "/x/{param}"-style, relative to BasePath
    public string HandlerType { get; set; } = "flow";  // flow | attributeDomain | eav | dataExchange (string, validated — no enum)
    public string? FlowId { get; set; }                // handler=flow: flow id or name
    public string? DomainName { get; set; }            // handler=attributeDomain|eav: overrides API-level AttributeDomain
    public string? ProfileId { get; set; }             // handler=dataExchange: profile id or name
    public string? Description { get; set; }
}

public sealed class DynamicApiDefinition
{
    public string Id { get; set; } = "";               // sanitized [A-Za-z0-9._-], globally unique PK
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string NodePath { get; set; } = "";         // workspace node, 1–3 segments: "Org" | "Org/Project" | "Org/Project/Sub"
    public string BasePath { get; set; } = "/";        // must start with '/', no trailing '/' unless exactly "/"
    public string? AttributeDomain { get; set; }       // API-level domain binding (fallback for ops)
    public string? BearerToken { get; set; }           // null/empty = no auth required
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<DynamicApiOperation> Operations { get; set; } = new();
}

public interface IDynamicApiStore
{
    IReadOnlyList<DynamicApiDefinition> GetAll(string? nodePathPrefix = null); // prefix match on NodePath, '/'-segment aware; sorted by Id
    DynamicApiDefinition? GetById(string id);
    (string Id, bool Created) Save(DynamicApiDefinition def); // upsert: existing Id → update in place (CreatedAt preserved); else same Name+NodePath → reuse its Id; else new Id = SanitizeId(Id ?? slug(Name)) uniquified with -2,-3 across the whole table
    bool Delete(string id);
}
```

`SqliteDynamicApiStore(string dbPath, ILogger<SqliteDynamicApiStore>? logger = null)` — copy the constructor pattern from `StepFunctions/SqliteAttributeDomainStore.cs:25-31`: `_dbPath = Path.GetFullPath(dbPath)`, then `using var conn = StepFlowDataDb.Open(_dbPath); StepFlowDataDb.EnsureSchema(conn);` and run this store-owned DDL (idempotent, private const string):

```sql
CREATE TABLE IF NOT EXISTS dynamic_apis (
  api_id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  description TEXT NOT NULL DEFAULT '',
  node_path TEXT NOT NULL,
  base_path TEXT NOT NULL,
  attribute_domain TEXT,
  bearer_token TEXT,
  is_active INTEGER NOT NULL DEFAULT 1,
  operations_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_dynamic_apis_node ON dynamic_apis(node_path);
```

`operations_json` = the `Operations` list serialized with Newtonsoft `CamelCasePropertyNamesContractResolver`. All methods take a private `_lock` and open one connection per call (same as `SqliteAttributeDomainStore`). Dates stored as ISO-8601 UTC strings (`"o"` format). `GetAll(nodePathPrefix)` filters `node_path = $p OR node_path LIKE $p || '/%'`.

### Step 2 — EAV row CRUD extension (`StepFunctions/EavRowStore.cs`)

`EavRowStore` (lines 53–106) already has private static `LoadRows(path)` / `WriteRows(path, rows)` and the lock + atomic-rewrite pattern in `AppendRow`. Add two public methods following that exact pattern:

```csharp
/// <summary>Replaces Values of an existing row by RowKeyId. Returns false when the domain file or row is missing.</summary>
public bool UpdateRow(string domainName, string rowKeyId, JObject values)
/// <summary>Merges `patch` properties into an existing row's Values (existing keys overwritten). False when missing.</summary>
public bool PatchRow(string domainName, string rowKeyId, JObject patch)
/// <summary>Removes a row by RowKeyId. Returns false when the domain file or row is missing.</summary>
public bool RemoveRow(string domainName, string rowKeyId)
```

Each: `lock (_lock)` → `RequirePath(domainName)` → `LoadRows` → find row (ordinal-ignore-case on RowKeyId) → mutate/remove list → `WriteRows`. No other changes to this file.

### Step 3 — Workspace helper (`Workspace/WorkspaceStore.cs`)

Add one public method after `ListProfiles` (~line 332), reusing existing `ListOrgs/ListProjects/ListSubProjects`:

```csharp
/// <summary>All depth-3 sub-project paths ("org/project/sub") under the root, ordinal-ignore-case sorted.</summary>
public IReadOnlyList<string> ListAllSubProjects()
{
    var result = new List<string>();
    foreach (var org in ListOrgs())
        foreach (var project in ListProjects(org))
            foreach (var sub in ListSubProjects(org, project))
                result.Add(Join(org, project, sub));
    return result.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
}
```

### Step 4 — Dispatcher (`DynamicApi/DynamicApiMatcher.cs`, `DynamicApi/DynamicApiDispatcher.cs`)

**`DynamicApiMatcher`** — pure static class (no DI), unit-testable:

```csharp
public sealed record MatchedOperation(DynamicApiDefinition Api, DynamicApiOperation Operation, JObject PathParams);

public static class DynamicApiMatcher
{
    // Full path = Normalize(Api.BasePath + Operation.Path). Returns best match or null.
    public static MatchedOperation? Match(string method, string requestPath, IReadOnlyList<DynamicApiDefinition> apis);
    // Methods allowed at a path across all active APIs (for 405 Allow header); empty when the path matches nothing.
    public static IReadOnlySet<string> AllowedMethods(string requestPath, IReadOnlyList<DynamicApiDefinition> apis);
}
```

Matching rules: split full path and request path on '/' into non-empty segments; equal segment counts required; each template segment is either literal (ordinal compare) or `{param}` where param matches `^[A-Za-z_][A-Za-z0-9_]*$` (captures the actual segment value into `PathParams`). Among multiple candidates pick highest count of literal segments, tie-break by `Api.Id` ordinal. `Normalize`: collapse duplicate slashes, ensure leading '/', strip trailing '/' unless result is exactly "/".

**`DynamicApiDispatcher`** — singleton class:

```csharp
public sealed class DynamicApiDispatcher
{
    public DynamicApiDispatcher(IDynamicApiStore store, StepFunctionService stepService, IAttributeDomainStore domainStore,
        EavRowStore eavRows, DataExchangeExecutor dataExchange, WorkspaceStore workspace);
    public Task HandleAsync(HttpContext context);
}
```

`HandleAsync` pipeline (write as one method with small private helpers):
1. `var rest = context.Request.RouteValues["rest"]?.ToString() ?? ""; var path = "/" + rest;`
2. `var apis = _store.GetAll();` (active only — store filters `is_active=1` in GetAll).
3. Match via `DynamicApiMatcher.Match(method, path, apis)`. No match: if `AllowedMethods(path)` non-empty → 405 with `Allow` header (comma-joined sorted) and `{ "error": "Method not allowed" }`; else 404 `{ "error": $"No dynamic API matches {method} {path}" }`.
4. **Auth**: if `matched.Api.BearerToken` non-empty: read `Authorization` header; must be exactly `Bearer <token>` (single space, ordinal-ignore-case scheme); compare token with `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` on UTF-8 bytes. Failure → 401, response header `WWW-Authenticate: Bearer realm="stepflow-dynamic"`, body `{ "error": "Invalid or missing bearer token" }`.
5. **Build input** (for flow/dataExchange handlers): read request body only when method is POST/PUT/PATCH and content-type contains `application/json` → parse with `JToken.Load`; unparseable/non-object → 400 `{ "error": "Body must be a valid JSON object" }`. Merge into one JObject: start from body properties, then overlay query-string values (each as string), then overlay `PathParams` (path params win). GET/DELETE input = query + path params only.
6. **Execute** by `Operation.HandlerType` (private methods, each returns `(int Status, JToken? Body)`):

   - **flow**: resolve definition via helper below; null → 404 `{ "error": $"Flow '{op.FlowId}' not found" }`. Run `var execution = await _stepService.ExecuteSyncAsync(op.FlowId!, input);` map: `Succeeded` → (200, `execution.Output`); `Failed|TimedOut|Aborted` → (500, `{ "error": execution.ErrorCode ?? "States.Failed", "message": execution.ErrorMessage }`); `Suspended` → (202, `{ "status": "suspended", "executionId": execution.ExecutionId }`).
   - **dataExchange**: `var result = await _dataExchange.ExecuteAsync(op.ProfileId!, input);` → (200, result). Catch `KeyNotFoundException` → 404 `{ "error": $"Profile '{op.ProfileId}' not found" }`; other exceptions → 500 `{ "error": ex.Message }`.
   - **attributeDomain**: effective domain = `op.DomainName ?? matched.Api.AttributeDomain` (guaranteed non-null by save-time validation). GET: `_domainStore.GetByName(name)`; null → 404 `{ "error": $"Attribute domain '{name}' not found" }`; else 200 with the same shape as `GET /api/attribute-domains` items — reuse `AttributeDomainMapping.FromPoco(domain, schema)` (see `Controllers/AttributeDomainsController.cs:27-29`). POST/PUT/PATCH: body must deserialize to `AttributeDomainEntry` (existing DTO, `StepFunctions/AttributeDomainEntry.cs`); if its `AttributeDomain.AttributeDomainName` is empty set it to the effective name; if it differs from the effective name → 400 `{ "error": "Body domain name does not match operation target" }`. Call `_domainStore.Save(entry.AttributeDomain, entry.SchemaDefinition != null ? <map ref via AttributeDomainMapping> : null)` — reuse whatever mapping `AttributeDomainsController.Save` uses (read it first; mirror exactly). → 200 `{ "status": "saved", "domainName": name }`. DELETE: `_domainStore.Delete(name)` false → 404, else 200 `{ "status": "deleted", "domainName": name }`.
   - **eav**: effective domain as above. GET modes are decided by `EavGetMapper.Map(opPath, pathParams, rawQuery)` (shared Core logic, so in-process and external hosts behave identically): no path params → Collection — the full query language applies verbatim (`EavQuery.Parse`/`Apply` over `_eavRows.ListRows(name)`: any non-reserved key filters rows by field equality on top-level row fields or captured `Values` keys; multiple values for one key OR, different keys AND; reserved: `sort`, `page`, `limit`, `offset`, `fields`; `entityId` is an ordinary filter key; `sort=capturedAtUtc,-entityId` comma list with `-` prefix descending, unknown keys no-ops, missing values sort first; pagination via `limit` (default 100), `offset`, or 1-based `page` — `page`+`offset` together → 400 `{ "error": … }`; `fields=a,b` projects rows to those wire names, `rowKeyId` always kept) → 200 `{ "rows": [...], "count": <total after filter> }`. One path param on an empty or one-segment op path (`{id}`) → Lookup — single-row by EntityId (ordinal), falling back to RowKeyId; only `fields` is honored from the query string; 404 `{ "error": $"Row '{id}' not found" }` when nothing matches, an object for a unique match, an array when the entityId matched several rows. One path param on a multi-segment op path (`{id}/comments`) → EntityFilter — collection with `entityId={value}` merged into the query (a same-value client pair is deduped; a different value conflicts → 400), full query language applied. Two or more path params are invalid for eav GET ops (rejected at save time). POST: body object; if it has top-level `entityId`/`entityType` properties extract them into the `EavRow`, everything else becomes `Values`; `_eavRows.AppendRow(name, row)` → 201 with the created row (incl. assigned RowKeyId). PUT `/…/{rowKeyId}`: body = new Values → `_eavRows.UpdateRow` false → 404 `{ "error": $"Row '{rowKeyId}' not found" }`, else 200 updated row. PATCH: `_eavRows.PatchRow`. DELETE: `_eavRows.RemoveRow` false → 404, else 200 `{ "status": "deleted", "rowKeyId": … }`.

   **Flow resolution helper** (private in dispatcher) — required because startup only recovers checkpoints (`StepFunctionService.ExecuteAsync` line 117–127); workspace flows are not auto-registered:
   ```csharp
   private StateMachineDefinition? ResolveFlow(string flowIdOrName)
   {
       var sm = _stepService.GetStateMachine(flowIdOrName);
       if (sm != null) return sm.Definition;
       foreach (var sub in _workspace.ListAllSubProjects())
           if (_workspace.LoadFlowDefinition(sub, flowIdOrName) is { Length: > 0 } json)
           {
               var doc = JObject.Parse(json);
               var statesObj = doc["states"] as JObject ?? continue;
               var meta = _workspace.ListFlows(sub).FirstOrDefault(f => f.Id == flowIdOrName).Meta; // may be default when missing — guard null
               var def = new StateMachineDefinition { StartAt = doc["startAt"]?.ToString() ?? statesObj.Properties().First().Name, States = statesObj.ToObject<Dictionary<string, StateDefinition>>()! };
               _stepService.RegisterStateMachine(meta?.Name ?? flowIdOrName, def, meta?.Description, id: flowIdOrName);
               return def;
           }
       return null;
   }
   ```
7. Write response: `context.Response.StatusCode = status; context.Response.ContentType = "application/json"; await context.Response.WriteAsync(body.ToString(Formatting.None));` Log every request at Information via injected `ILogger<DynamicApiDispatcher>`: method, path, api id, handler, status, elapsed ms.

### Step 5 — OpenAPI generator (`DynamicApi/DynamicApiOpenApiGenerator.cs`)

```csharp
public static class DynamicApiOpenApiGenerator
{
    public static JObject Build(IReadOnlyList<DynamicApiDefinition> apis, IAttributeDomainStore domains);
}
```

Emit OpenAPI `3.0.1`:
- `info`: `{ title: "StepFlow Dynamic APIs", version: "1.0" }`; `servers`: `[ { url: "/api/dynamic" }]`.
- For each API (sorted by NodePath then Name) and each operation: path key = normalized `basePath + op.Path` **without** the `/api/dynamic` prefix; method lowercase. `tags: [api.Name]`, `summary: op.Description ?? $"{op.Method} {path}"`.
- Parameters: one per `{param}` template segment → `{ name, in: "path", required: true, schema: { type: "string" } }`. eav GET is mode-aware (`EavGetMapper`): Collection and EntityFilter ops get the full query language — `entityId`, `sort`, `page`, `limit` (default 100), `offset`, `fields`; Lookup ops get only `fields` plus an operation description of the single-row contract; EntityFilter ops additionally carry a description noting the entityId filter from the path parameter.
- `requestBody` for POST/PUT/PATCH (`required: true`, content `application/json`): attributeDomain → `$ref` to the domain schema component; eav POST → `{ type: "object", description: "Row values; optional top-level entityId/entityType" }`; flow/dataExchange → `{ type: "object", description: "Free-form input (path params and query are merged in)" }`.
- `responses`: 200 (or 201 for eav POST) with schema — attributeDomain GET/POST → domain `$ref` / `{ type: "object" }`; eav GET collection → `{ type: "object", properties: { rows: array of EavRow refs, count: integer } }`; eav GET lookup → `{ anyOf: [EavRow ref, array of EavRow refs] }` (object for a unique match, array on several); flow/dataExchange → `{ type: "object" }`. Plus `401` (only when the API has a token), `404`, `500` with generic descriptions.
- `security`: per operation `[ { bearerAuth: [] }]` only when that API's BearerToken is non-empty; `components.securitySchemes.bearerAuth = { type: "http", scheme: "bearer" }` (emit the component only if at least one API has a token).
- Domain schema components, one per distinct effective domain referenced by any attributeDomain/eav operation: name `DynamicApi_{DomainName}`; `type: object`; properties from `_domainStore.GetByName(name)` attributes — type map on `EntityAttribute.DataType` ordinal (per `StepFunctions/AttributeDomainEntry.cs:41` comment): 0→string, 1→boolean, 2→number, 3→`{ type: "string", format: "date-time" }`, 4→object, 5→array; `description` from DisplayName/Description; `required` list = attributes whose `ValidationSchemaJson` parses as JSON with `"required": true`. Skip the component when the domain doesn't exist (operation still emitted with free-form object schema).
- Also emit a static `EavRow` component: `{ type: "object", properties: { rowKeyId, entityId, entityType, sourceTaskId, capturedAtUtc (string date-time), values (object) } }`.

### Step 6 — Management controller (`Controllers/DynamicApisController.cs`)

Follow the existing attribute-route controller pattern (`[ApiController]`, explicit routes, `new { error = … }` bodies). Inject `IDynamicApiStore` + `WorkspaceStore`:

- `[HttpGet("api/dynamic/apis")]` `List([FromQuery] string? nodePath)` → `Ok(_store.GetAll(nodePath))`.
- `[HttpPost("api/dynamic/apis")]` `Save([FromBody] JObject payload)` — validate in this order, first failure wins:
  1. `name` non-empty else 400; `nodePath`: split on '/', 1–3 segments, each free of `Path.GetInvalidFileNameChars()` and not "."/"..", and `_workspace.NodeExists(nodePath)` else 404 `{ "error": $"Workspace node '{nodePath}' not found" }`.
  2. `basePath` starts with '/' (default "/"), no spaces; operations array non-empty; each op: Method ∈ {GET,POST,PUT,PATCH,DELETE}; Path "" or starts with '/'; template params match `^[A-Za-z_][A-Za-z0-9_]*$`; HandlerType ∈ {flow,attributeDomain,eav,dataExchange}; required config present — flow→`flowId`, dataExchange→`profileId`, attributeDomain/eav→(`domainName` ?? payload-level `attributeDomain`) non-empty. Any violation → 400 with the specific field in the message.
  3. Route conflict: for each op compute normalized full path; if any *other* active API (different id than the one being updated) has same method+fullPath → 409 `{ "error": $"Route conflict: {method} {path} already defined by api '{otherId}'" }`.
  Then `_store.Save(def)` → `Ok(new { id, created })` (201 when created).
- `[HttpGet("api/dynamic/apis/{id}")]` → definition or 404.
- `[HttpDelete("api/dynamic/apis/{id}")]` → `{ "deleted": true }` or 404.

### Step 7 — Program.cs wiring

In `Startup.ConfigureServices`, immediately after the `IAttributeDomainStore` registration block (Program.cs:130–136):

```csharp
// Dynamic API subsystem — user-defined REST endpoints backed by flows / attribute domains / EAV rows / data-exchange profiles.
services.AddSingleton<IDynamicApiStore, SqliteDynamicApiStore>(provider => new SqliteDynamicApiStore(
    provider.GetRequiredService<IOptions<FormDataOptions>>().Value.DatabasePath,
    provider.GetService<ILogger<SqliteDynamicApiStore>>()));
services.AddSingleton<DynamicApiDispatcher>();
```

(`using StepFunctionsApp.DynamicApi;` at top.) In `Startup.Configure`, inside the existing `app.UseEndpoints(endpoints => { … })` block (Program.cs:184–219), after `MapControllerRoute`:

```csharp
// Dynamic API surface — OpenAPI spec + catch-all dispatcher. Literal routes above win over the catch-all.
var openApiGenerator = app.ApplicationServices.GetRequiredService<IDynamicApiStore>();
endpoints.Map("/api/dynamic/openapi.json", context =>
{
    var domains = context.RequestServices.GetRequiredService<IAttributeDomainStore>();
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(DynamicApiOpenApiGenerator.Build(openApiGenerator.GetAll(), domains).ToString(Newtonsoft.Json.Formatting.None));
});
var dispatcher = app.ApplicationServices.GetRequiredService<DynamicApiDispatcher>();
endpoints.Map("/api/dynamic/{**rest}", async context => await dispatcher.HandleAsync(context));
```

### Step 8 — Builder UI (StepFlow-UI)

The live builder is the React app in `StepFlow-UI/` (vite builds to `../dist`, served at `/` by Program.cs; dev proxy `/api` → :5001). Add a panel following the WorkspacePanel/DataExchangePanel pattern.

1. **`src/services/dynamicApiService.ts`** — mirror `workspaceService.ts` structure (interfaces + private `fail(res)` helper + class with static methods):
   - Interfaces: `DynamicApiOperation`, `DynamicApiDefinition` (camelCase, matching Step 1).
   - `class DynamicApiService { static async list(nodePath?: string); static get(id); static save(def): Promise<{ id: string; created: boolean }>; static remove(id); static openApi(): Promise<Record<string, unknown>>; }` — plain `fetch('/api/dynamic/…')`, throw on `!res.ok` with the server's `error` field.
2. **`src/components/DynamicApi/DynamicApiPanel.tsx`** — full-height panel (copy layout conventions from `WorkspacePanel.tsx`: header bar with title + close X, two-column body). Left column: workspace node tree — reuse `useWorkspaceStore` (`loadTree()`, tree state) but allow selecting **any depth 1–3** node (org/project/sub); below it the list of APIs for that node (`DynamicApiService.list(nodePath)`), each row showing name, basePath, lock icon when bearerToken set; "New API" and per-row Delete buttons. Right column editor form: Name, Description, BasePath (text, default "/"), AttributeDomain (select from `GET /api/attribute-domains` names + "(none)"), Bearer Token (password input + "Generate" button using `crypto.getRandomValues` → 32 hex chars + copy-to-clipboard), IsActive checkbox; Operations table — one row per op with: Method select, Path text (placeholder "/{id}"), HandlerType select, then handler-specific fields rendered conditionally: flow→FlowId select (options from `GET /api/flows`, value = id, label = name); dataExchange→ProfileId select (`GET /api/data-exchange/profiles`); attributeDomain/eav→DomainName select (defaults to the API-level domain). Add/remove op rows. Save button → `DynamicApiService.save`. Below the editor, two tabs:
   - **OpenAPI**: "Load" fetches `/api/dynamic/openapi.json`, renders pretty JSON in a `<pre>` with a copy-URL button (`/api/dynamic/openapi.json`).
   - **Test**: pre-filled Method + URL (from selected op or the API's first GET op) + Authorization input auto-filled with `Bearer <token>`; Send → shows status code and response body in a `<pre>`. Same-origin, no CORS needed.
3. **`src/App.tsx`** — add `const [showDynamicApiPanel, setShowDynamicApiPanel] = useState(false);` next to the other panel states (lines 38–41); render `{showDynamicApiPanel && (<DynamicApiPanel onClose={() => setShowDynamicApiPanel(false)} />)}` right after the WorkspacePanel block (~line 606–611); pass `onToggleDynamicApi={() => setShowDynamicApiPanel((p) => !p)}` to `<AppHeader>` (props list at ~line 434).
4. **`src/components/Header/AppHeader.tsx`** — add optional prop `onToggleDynamicApi?: () => void;` and, after the Workspace button block (lines 153–157): `{onToggleDynamicApi && (<button className="btn-icon" title="Toggle Dynamic API Panel" onClick={onToggleDynamicApi}><Globe className="w-4 h-4" /></button>)}` — import `Globe` from lucide-react in the existing import (line 1).

### Step 9 — Tests (`StepFunctionsApp.Tests/`)

Follow existing patterns (temp dirs for stores, xunit `[Fact]`, `InternalsVisibleTo` already set):
- **`DynamicApiMatcherTests.cs`** — pure matcher: literal match; `{param}` capture into PathParams; method mismatch → null with `AllowedMethods` populated; most-literal-segments wins; empty op path matches basePath exactly; trailing-slash normalization.
- **`SqliteDynamicApiStoreTests.cs`** (pattern of `SchemaDefinitionStoreTests.cs`, temp dir db): Save creates id + timestamps; re-save same Name+NodePath preserves Id and updates UpdatedAt; explicit-id update in place; Delete true/false; GetAll nodePathPrefix filtering ("Acme" matches "Acme/P/S").
- **`DynamicApiOpenApiGeneratorTests.cs`** — two APIs (one with token, one without) + a real domain via `JsonFileAttributeDomainStore` over a temp file: paths present under correct keys; `securitySchemes.bearerAuth` emitted; per-op security only on the tokened API; domain component has expected property types (String→string, Number→number, Date→date-time string); eav GET response is array of EavRow.
- **`EavRowStoreCrudTests.cs`** — temp dir: UpdateRow replaces Values / false for unknown key; PatchRow merges without dropping other keys / false when missing; RemoveRow removes / false twice.
- **`DynamicApiEndpointTests.cs`** — integration via `WebApplicationFactory<Program>`, copy the factory setup from `McpEndpointTests.cs` exactly (content root, unique-name helper). Cases: create an eav API with bearer token bound to a uniquely-named domain + node "Default" → request without token = 401 with WWW-Authenticate header; wrong token = 401; correct token = 200 and `count` ≥ rows appended in-test; POST row = 201 then visible in GET; DELETE api cleanup in `Dispose`; `/api/dynamic/openapi.json` contains the created path key and `bearerAuth`.

## Critical files & anchors

- `Program.cs:130-153, 184-219` — DI registration point (after IAttributeDomainStore block) and endpoints block where the catch-all + openapi routes go.
- `StepFunctions/StepFlowDataDb.cs:17-69` — `Open`/`EnsureSchema`; the new store must call both before its own DDL.
- `Workspace/WorkspaceStore.cs:209-332` — flow storage under subprojects (`LoadFlowDefinition`, `ListFlows`) used by lazy flow registration; `ListAllSubProjects` goes after `ListProfiles`.
- `StepFunctions/EavRowStore.cs:53-106` — lock + atomic rewrite pattern the three new row methods copy.
- `StepFlow-UI/src/App.tsx:38-41, 434-439, 602-611` + `AppHeader.tsx:150-157` — panel state/toggle wiring to replicate for the new panel.

## Verification

Working directory: `C:/Source/stepflow-builder/StepFunctionsApp`.

1. Build: `dotnet build StepFunctionsApp.csproj` → 0 errors.
2. Unit + integration tests: `dotnet test StepFunctionsApp.Tests --filter "FullyQualifiedName~DynamicApi|FullyQualifiedName~EavRowStoreCrud"` all pass; then full `dotnet test StepFunctionsApp.Tests` (no regressions).
3. UI build: `cd StepFlow-UI && npm run build` → succeeds, emits to `../dist`.
4. End-to-end (app running via `dotnet run --project StepFunctionsApp`, port 5001; FormData provider is "sqlite" per appsettings.json):
   - `curl http://localhost:5001/api/dynamic/apis` → `[]`.
   - Create API: `POST /api/dynamic/apis` body `{ "name": "Orders", "nodePath": "Default", "basePath": "/orders", "bearerToken": "secret-123", "attributeDomain": "OrderApproval", "operations": [ { "method": "GET", "path": "", "handlerType": "eav" }, { "method": "POST", "path": "", "handlerType": "eav" } ] }` → `{ id, created: true }`.
   - `curl http://localhost:5001/api/dynamic/orders` → **401** + `WWW-Authenticate: Bearer realm="stepflow-dynamic"`; with `-H "Authorization: Bearer secret-123"` → **200** `{ rows: [...], count: N }` where rows match `eav-data/OrderApproval.json`.
   - `POST /api/dynamic/orders` (token, body `{"orderId":"ORD-999","customerName":"E2E"}`) → **201** with RowKeyId; GET shows it.
   - Route conflict: second API with same method+path → **409**.
   - `curl http://localhost:5001/api/dynamic/openapi.json` → valid OpenAPI containing `/orders`, `securitySchemes.bearerAuth`, and a `DynamicApi_OrderApproval` schema component.
   - Flow handler: register an echo flow via existing `POST /api/flows` (any trivial 2-state flow), create a second API with `{ "method": "GET", "path": "/run/{orderId}", "handlerType": "flow", "flowId": "<that id>" }`, call it → **200** with the flow's Output.
5. UI: open `http://localhost:5001` (serves dist/index.html), click the new Globe header button → Dynamic API panel opens; select node "Default" → "Orders" listed; edit + save round-trips; OpenAPI tab renders the spec; Test tab sends a request and shows 200/401 correctly.

## Assumptions & contingencies

- **Per-API bearer token, optional**: each dynamic API carries its own token (empty = open access), matching the app's current zero-auth posture and the "create Dynamic APIs that can be protected via a Bearer Token" reading. A global token is emulable by setting the same value on all APIs; no schema change needed if that becomes the requirement.
- **Mount prefix**: all dynamic endpoints live under `/api/dynamic/` (spec at `/api/dynamic/openapi.json`) to avoid colliding with existing controllers and keep auth self-contained. User-facing paths are `basePath + opPath`, e.g. `/api/dynamic/orders/{orderId}`.
- **Storage**: definitions always in SQLite at `FormData:DatabasePath` (`stepflow_data.db`), independent of `FormData.Provider`; attributeDomain handlers still honor the configured provider (sqlite or json) via `IAttributeDomainStore`.
- **"OU" = workspace org node**; APIs attach at any depth 1–3 via `node_path`, validated against `WorkspaceStore.NodeExists` at save time.
- **Flow input merge precedence**: path params > query string > JSON body; response is `execution.Output` verbatim (transformation happens inside the flow / DataExchange pipeline, per the request).
- If a flow handler's id matches neither the in-memory registry nor any workspace sub-project flow file → 404 with the flow id in the message (no auto-creation of flows).
- If `FormData.Provider` is "json" at runtime, `stepflow_data.db` may not pre-exist; `StepFlowDataDb.Open` creates it on first use — no special handling needed.

## AI-Assisted Wizard (implemented)

The Dynamic API panel has an **AI Wizard** button (Sparkles icon, header bar) that opens a four-phase guided creation flow (`StepFlow-UI/src/components/DynamicApi/AiDynamicApiWizard.tsx`):

1. **Shell** — name, workspace node (any depth 1–3), basePath, api-level attributeDomain (pre-filled from the node's first domain), bearer token (generate/copy).
2. **Operations** — pick a REST method + one-line intent; the local model (saved AI config) generates ONE operation draft grounded in real context: attribute domains + attributes, up to 3 sample EAV rows per domain, workspace flows, data-exchange profiles (`StepFlow-UI/src/services/aiDynamicApiBuilder.ts` builds prompt + user message). The draft is validated client-side against the same rules as `DynamicApisController.Save` (method/path/template-param/handler requirements, eav GET ≤1 `{param}`, reserved `/apis` prefix, in-API route conflicts) and is **always editable** — AI output never bypasses validation.
3. **Review & Save** — full definition table; save via the standard `POST /api/dynamic/apis`; server errors (e.g. cross-API 409 route conflict) are shown verbatim with a regenerate shortcut.
4. **Test & Deploy** — test any operation against the in-process engine (`/api/dynamic/…`, same-origin) or a user-supplied `DynamicApiHost` base URL (cross-origin; the host is CORS-enabled for this), plus publish + curl-command copy for UAT/Docker targets.

Supporting changes:
- `dynamicApiService.ts`: context fetch helpers used by the wizard — `domains()`, `flows()`, `profiles()`, `eavSampleRows(domain, limit)`.
- `DynamicApiHost/Program.cs`: permissive CORS policy (`AllowAnyOrigin` + any header/method) so the wizard's Phase 4 can call a remote host; Bearer auth still gates every dynamic route and the backend engine stays same-origin only. Covered by two facts in `StepFunctionsApp.Tests/DynamicApiHostTests.cs`.
- `smoke/mock-lmstudio.cjs`: canned eav op-draft response for the wizard's prompt (method taken from the user message), so the browser smoke test exercises generation without a real model.
