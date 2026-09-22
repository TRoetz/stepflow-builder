# StepFlow.DataExchange

Data Exchange pipeline engine: profile store with file-watch reload, multi-source extraction (SQL Server / CSV / HTTP), DuckDB transforms, routing and dispatch, plus an execution log. This is what backs `dataexchange://` resources in ASL flows and the builder's DataExchange UI.

## Package

| | |
|---|---|
| **Id** | `StepFlow.DataExchange` |
| **Version** | 1.0.0 |
| **Target framework** | net10.0 |

## Dependencies

Project references:

- `StepFlow.Metadata` — the profile/data-source/pipeline models (`StepFlow.DataModel.Entities.DataSource.*`) and attribute-domain stores
- `StepFlow.Transform` — DuckDB transform service used by pipeline stages

NuGet:

- `Microsoft.Data.SqlClient` 6.0.2 — SQL Server source
- `Microsoft.Extensions.Hosting.Abstractions` 10.0.5 (file monitor is a `BackgroundService`)
- `Microsoft.Extensions.Http` 10.0.5 — HTTP source via `IHttpClientFactory`
- `Microsoft.Extensions.Logging.Abstractions` 10.0.5
- `Newtonsoft.Json` 13.0.4

## What's inside

| Type | Role |
|---|---|
| `DataExchangeProfileStore` | Finds profile JSON documents in the workspace tree (`<sub>/data-exchange/<profileId>/profile.json`, plus an optional legacy flat directory). `LoadAll()`, `Get(idOrName)` — reads from disk on every lookup, so edits to a profile file take effect on the next execution without a restart. |
| `DataExchangeExecutor` | The engine entry point: resolves a profile by id/name and runs its pipeline (extract → transform → route/dispatch), returning the result as JSON. Also used directly by the `dataexchange://` resource scheme in ASL flows. |
| `DataExchangeFileMonitorService` | `BackgroundService` that polls the inbox directory for uploaded customer files, picks up matching profiles, executes them, and guards against re-processing in-flight files. |
| `DataExchangeExecutionLog` | In-memory + persisted log of profile executions (status, row counts, errors) surfaced by the builder's REST/MCP endpoints. |
| `DataExchangeOptions` | Options section (`"DataExchange"`): poll interval, profiles/inbox/output directories. |

## Usage

```xml
<PackageReference Include="StepFlow.DataExchange" Version="1.0.0" />
```

```csharp
using StepFunctionsApp.DataExchange;   // original namespace kept for compatibility
using StepFunctionsApp.StepFunctions;  // DuckDbTransformService (from StepFlow.Transform)

var configuration = /* your IConfiguration */;

services.Configure<DataExchangeOptions>(configuration.GetSection(DataExchangeOptions.SectionName));
services.AddSingleton<DuckDbTransformService>();
services.AddHttpClient();                                   // HTTP source
services.AddSingleton<DataExchangeProfileStore>(sp =>
    new DataExchangeProfileStore(workspaceRoot, legacyProfilesDirectory: null));
services.AddSingleton<DataExchangeExecutionLog>();
services.AddSingleton<DataExchangeExecutor>();

// Optional: hosted inbox monitor (ASP.NET host)
services.AddHostedService<DataExchangeFileMonitorService>();

// ...
var executor = app.Services.GetRequiredService<DataExchangeExecutor>();

// Run a profile by id or name — same call the dataexchange:// scheme makes
JObject result = await executor.ExecuteAsync("stock-sync", input: null);

// Or run an in-memory profile object directly (e.g. one you just built)
var profile = app.Services.GetRequiredService<DataExchangeProfileStore>().Get("stock-sync")!;
result = await executor.ExecuteProfileAsync(profile, new JObject { ["batchId"] = "b-1" });
```

## Notes

- **Profiles are data, not code**: each profile is a JSON document (full anatomy in `docs/StepFlow_Usage_Guide.md` §4) describing source → pipeline stages → dispatch. Profiles are read from disk per lookup (no cache), so editing one on disk takes effect immediately; the *inbox* monitor (`DataExchangeFileMonitorService`) is what polls for uploaded customer files.
- SQL Server connections come from the profile's data-source definition; keep credentials out of flow/profile JSON in shared environments (the builder supports environment-variable expansion for connection strings).
- Types live in their original namespaces (`StepFunctionsApp.DataExchange`, `StepFlow.DataModel.Entities.DataSource`) — the package split moved files, not namespaces.
