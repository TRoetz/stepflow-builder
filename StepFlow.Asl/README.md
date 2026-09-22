# StepFlow.Asl

The ASL (Amazon States Language dialect) durable workflow engine — the heart of StepFlow. Executes state machines with checkpointing and auto-resume, dispatches every resource scheme (`http(s)://`, `sql://`, `duckdb://`, `jsonata://`, `script://`, `eav://`, `rule://`, `rules://`, `dataexchange://`, `ssh://`, `fetch://`, `internal://`, …), and ships the named-rule catalog, rule engines, human-task gates, durable flow-state persistence (disk or Redis) and BPMN conversion.

This is the top of the package dependency graph: it references every other StepFlow component, so a single reference gives you the full engine.

## Package

| | |
|---|---|
| **Id** | `StepFlow.Asl` |
| **Version** | 1.0.0 |
| **Target framework** | net10.0 |

## Dependencies

Project references (all in this repo):

- `StepFlow.Metadata` — attribute-domain/schema models + stores, shared infra types
- `StepFlow.Transform` — DuckDB transforms (`duckdb://`, transform stages)
- `StepFlow.Eav` — EAV registry/row store for `eav://` resources
- `StepFlow.Forms` — form definitions + validation (form-capture states)
- `StepFlow.DataExchange` — pipeline engine for `dataexchange://` resources

NuGet:

| Package | Used for |
|---|---|
| `Jsonata.Net.Native.JsonNet` 3.0.0 | `jsonata://` transforms |
| `Microsoft.CodeAnalysis.CSharp.Scripting` 4.8.0 | `script://` C# script tasks |
| `RulesEngine` 6.0.1 | MS-Rules workflows (`rules://`) |
| `SSH.NET` 2024.1.0 | `ssh://` command execution |
| `StackExchange.Redis` 3.0.11 | optional Redis flow-state backend |
| `Microsoft.Data.Sqlite` 10.0.11 | in-memory SQLite database behind the SQL rule engine |
| `Newtonsoft.Json` 13.0.4 | payload handling everywhere |
| `Microsoft.Extensions.*` (Configuration, FileProviders, FileSystemGlobbing, Hosting.Abstractions, Http, Logging.Abstractions, Options) — 10.0.5 | DI/hosting abstractions for the services below |

## What's inside

| Area | Types |
|---|---|
| **Core execution** | `StepFunctionInterpreter` (state machine loop, payload pipeline, retry/catch), `StepFunctionService` (`BackgroundService`: register machines, start/execute/resume executions, recover on boot), `StatesLanguageModels.cs` / `ExecutionModels.cs` (ASL + execution models) |
| **Resources** | `CompositeResourceInvoker` — dispatches all 14 resource schemes; plus the per-scheme services: `SqlDataSourceResolver`, `ScriptExecutionService`, `JsonataProcessor`, `AiDecisionService`, `SshCommandService` + `SshHostStore`, `FetchRemoteFilesService` |
| **Rules** | `RuleEngineService` (SQL rules, `rule://<name>`), `MicrosoftRulesEngineService` (`rules://<name>`), and the named-rule subsystem in `Rules/`: `NamedRule`, `INamedRuleStore`, `JsonFileNamedRuleStore` (`rules.json`), `NamedRuleManager` (persist + live-register with engines) |
| **Human tasks** | `HumanTasks.cs` / `IHumanTaskStore`, `HumanTaskCompletionProviders.cs` — suspend a flow on approval, resume via API or completion provider |
| **Persistence** | `FlowStateStore.cs`: `IFlowStateStore` + `DiskFlowStateStore` / `RedisFlowStateStore` implementations and `FlowStateOptions` (checkpointing, auto-resume) |
| **Conversion** | `BpmnConverter` / `BpmnModels.cs` — BPMN → ASL conversion used by the migration pipeline |

## Usage

```xml
<PackageReference Include="StepFlow.Asl" Version="1.0.0" />
```

### Wiring (minimal)

The engine is a graph of services; `CompositeResourceInvoker` pulls in one service per resource scheme, so register the siblings you actually use. The builder's `StepFunctionsApp/Program.cs` is the canonical full wiring — a minimal host looks like:

```csharp
using StepFunctionsApp.StepFunctions;   // original namespace kept for compatibility

services.AddHttpClient();

// EAV (StepFlow.Eav) — registry needs Initialize() before registration
var eavRegistry = new EavRegistryService();
eavRegistry.Initialize("eav_registry.json");
services.AddSingleton(eavRegistry);
services.AddSingleton<IEavEntityProvider, CompositeEavEntityProvider>();  // registry ∪ attribute domains
services.AddSingleton<EavRowStore>(sp => { var r = new EavRowStore(sp.GetService<ILogger<EavRowStore>>()); r.Initialize("eav-data"); return r; });

// SSH inventory (curated hosts for ssh:// resources)
var sshHosts = new SshHostStore();
sshHosts.Initialize("ssh_hosts.json");
services.AddSingleton(sshHosts);

// Data Exchange (StepFlow.DataExchange) — optional if you don't use dataexchange://
services.Configure<DataExchangeOptions>(config.GetSection(DataExchangeOptions.SectionName));
services.AddSingleton(new DataExchangeProfileStore(workspaceRoot, "dataexchange/profiles"));
services.AddSingleton<DataExchangeExecutor>();

// Scheme services (ASL)
services.AddSingleton<RuleEngineService>();               // in-memory SQLite rule DB (rule://)
services.AddSingleton<MicrosoftRulesEngineService>();     // MS-Rules workflows (rules://)
services.AddSingleton<DuckDbTransformService>();          // StepFlow.Transform
services.AddSingleton<ScriptExecutionService>();
services.AddSingleton<AiDecisionService>();
services.AddSingleton<SshCommandService>();
services.AddSingleton<FetchRemoteFilesService>();

// The engine
services.AddSingleton<IResourceInvoker, CompositeResourceInvoker>();
services.AddSingleton<StepFunctionInterpreter>();
services.AddSingleton<BpmnConverter>();
services.AddSingleton<StepFunctionService>();
services.AddHostedService(sp => sp.GetRequiredService<StepFunctionService>());  // recovery + auto-resume on boot
// Cycle breaker: the invoker needs a Lazy<StepFunctionService> for sub-flow invocation.
services.AddTransient<Lazy<StepFunctionService>>(sp => new Lazy<StepFunctionService>(() => sp.GetRequiredService<StepFunctionService>()));

// Optional but recommended — durable checkpoints + auto-resume (disk by default)
services.Configure<FlowStateOptions>(config.GetSection(FlowStateOptions.SectionName));
services.AddSingleton<IFlowStateStore>(sp => new DiskFlowStateStore(
    sp.GetRequiredService<IOptions<FlowStateOptions>>().Value.DiskPath,
    sp.GetService<ILogger<DiskFlowStateStore>>()));
```

### Running a flow

```csharp
var flows = app.Services.GetRequiredService<StepFunctionService>();

flows.RegisterStateMachine("hello", new StateMachineDefinition
{
    /* ASL JSON: states, transitions, resource URIs — see docs/StepFlow_Usage_Guide.md */
});

// Fire-and-track (checkpointed; survives restarts when a flow-state store is registered)
Execution run = flows.StartExecution("hello", JToken.Parse("""{ "name": "world" }"""));

// Or block until terminal status
Execution done = await flows.ExecuteSyncAsync("hello", null);
Console.WriteLine(done.Status);   // Succeeded | Failed | Suspended (human task waiting)
```

### Named rules

```csharp
var rules = app.Services.GetRequiredService<NamedRuleManager>();
rules.Save(new NamedRule { Name = "pricing/standard", Kind = "sql", /* … */ });
// rule://pricing/standard now resolves in any flow; rules.json persists it across restarts.
```

## Notes

- **Durability**: with an `IFlowStateStore` registered (disk by default, Redis optional via the `FlowState` config section), every state boundary is checkpointed; suspended and running executions are recovered on boot (`FlowState.AutoResume`).
- **Environment binding**: `sql://<name>` resolves through the `"SqlDataSources"` configuration section (logical name → database path) — flows never hard-code machine-specific paths.
- Types live in their original namespaces (`StepFunctionsApp.StepFunctions`, `StepFlow.DataModel.*`) — the package split moved files, not namespaces.
