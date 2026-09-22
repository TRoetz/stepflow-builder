using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.Rules;
using StepFunctionsApp.StepFunctions;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// STEPFLOW.ASL DEMO — the state-machine flow engine, wired exactly like a real host:
//   * full DI graph (CompositeResourceInvoker + per-scheme services)
//   * one flow exercising five resource schemes in sequence:
//       transform://jsonata  -> payload shaping
//       transform://csharp   -> C# script task (total calculation + SQL generation)
//       sql://orders-db      -> logical datasource name bound via config section
//       eav://Order          -> EAV row store write + read-back
//       HumanTask            -> suspend, checkpoint to disk, host restart, resume
//   * durable checkpoints: the flow is stopped mid-execution (suspended at the
//     human task) and a second host generation recovers it from disk and resumes.
//   * named rules persisted to rules.json survive the restart too.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

Console.WriteLine("+++ StepFlow.Asl demo +++");
Console.WriteLine("State-machine engine: jsonata -> csharp script -> sql:// -> eav:// -> human task, with durable checkpoints\n");

var ws = Path.Combine(Path.GetTempPath(), "stepflow-demo", "asl");
if (Directory.Exists(ws)) Directory.Delete(ws, true);
Directory.CreateDirectory(ws);
// SQLite database for sql://orders-db — the logical name binds to this path via config below.
var dbPath = Path.Combine(ws, "orders.db");
using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "CREATE TABLE IF NOT EXISTS orders (id INTEGER PRIMARY KEY AUTOINCREMENT, customer_name TEXT NOT NULL, items INTEGER NOT NULL, total REAL NOT NULL)";
    cmd.ExecuteNonQuery();
}

// -- 1. Configuration: logical SQL datasource binding ----------------------------
var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string>
    {
        ["SqlDataSources:orders-db"] = dbPath,
    })
    .Build();

// -- 2. EAV registry (Initialize before registration; entity used by eav://Order) -
var eavRegistry = new EavRegistryService();
eavRegistry.Initialize(Path.Combine(ws, "eav_registry.json"));
eavRegistry.RegisterEntity(new EavEntityDefinition
{
    EntityName = "Order",
    Description = "An order flowing through the ASL demo pipeline",
    Attributes = new List<EavAttributeDefinition>
    {
        new() { AttributeName = "name", DataType = "string" },
        new() { AttributeName = "items", DataType = "number" },
        new() { AttributeName = "total", DataType = "number" },
    },
});

// -- 3. The service graph (canonical minimal wiring from the package README) ------
var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning).AddConsole());
services.AddHttpClient();

// EAV (StepFlow.Eav) — the entity provider bridges the registry and the attribute-domain store.
services.AddSingleton(eavRegistry);
services.AddSingleton<IAttributeDomainStore>(sp => new JsonFileAttributeDomainStore(
    Path.Combine(ws, "attribute-domains.json"), sp.GetService<ILogger<JsonFileAttributeDomainStore>>()));
services.AddSingleton<IEavEntityProvider>(sp => new CompositeEavEntityProvider(
    sp.GetRequiredService<EavRegistryService>(),
    sp.GetRequiredService<IAttributeDomainStore>()));
services.AddSingleton<EavRowStore>(sp =>
{
    var rows = new EavRowStore(sp.GetService<ILogger<EavRowStore>>());
    rows.Initialize(Path.Combine(ws, "eav-data"));
    return rows;
});

// SSH inventory (ctor dependency of the invoker even when ssh:// is unused)
var sshHosts = new SshHostStore();
sshHosts.Initialize(Path.Combine(ws, "ssh_hosts.json"));
services.AddSingleton(sshHosts);

// Data Exchange (StepFlow.DataExchange) — ctor dependency; dataexchange:// unused here
services.AddSingleton(new DataExchangeProfileStore(ws));
services.AddSingleton<DataExchangeExecutor>();

// Per-scheme services
services.AddSingleton<RuleEngineService>();
services.AddSingleton<MicrosoftRulesEngineService>();
services.AddSingleton<DuckDbTransformService>();
services.AddSingleton<ScriptExecutionService>();
services.AddSingleton<AiDecisionService>();
services.AddSingleton<SshCommandService>();
services.AddSingleton<FetchRemoteFilesService>();

// Named rules (persisted to rules.json)
var ruleStore = new JsonFileNamedRuleStore();
ruleStore.Initialize(Path.Combine(ws, "rules.json"));
services.AddSingleton<INamedRuleStore>(ruleStore);
services.AddSingleton<NamedRuleManager>();

// Human tasks + durable flow-state checkpoints (disk)
services.AddSingleton<IHumanTaskStore>(sp =>
    new DiskHumanTaskStore(Path.Combine(ws, "flow-state", "human-tasks"), sp.GetService<ILogger<DiskHumanTaskStore>>()));
var flowStateOptions = new FlowStateOptions { DiskPath = Path.Combine(ws, "flow-state"), AutoResume = true };
services.AddSingleton<IOptions<FlowStateOptions>>(new FixedOptions(flowStateOptions));
services.AddSingleton<IFlowStateStore>(sp => new DiskFlowStateStore(
    sp.GetRequiredService<IOptions<FlowStateOptions>>().Value.DiskPath,
    sp.GetService<ILogger<DiskFlowStateStore>>()));

// The engine
services.AddSingleton<IResourceInvoker, CompositeResourceInvoker>();
services.AddSingleton<StepFunctionInterpreter>();
services.AddSingleton<BpmnConverter>();
services.AddSingleton<StepFunctionService>();
services.AddHostedService(sp => sp.GetRequiredService<StepFunctionService>());
// Cycle breaker: the invoker needs a Lazy<StepFunctionService> for sub-flow invocation.
services.AddTransient<Lazy<StepFunctionService>>(sp => new Lazy<StepFunctionService>(() => sp.GetRequiredService<StepFunctionService>()));

// -- 4. The flow definition --------------------------------------------------------
var flow = new StateMachineDefinition
{
    Comment = "Order intake: jsonata -> csharp script -> SQLite -> EAV -> human approval",
    StartAt = "enrich",
    States = new Dictionary<string, StateDefinition>
    {
        ["enrich"] = new()
        {
            Type = StateType.Task,
            Resource = "transform://jsonata",
            Parameters = JObject.Parse("""{ "expression": "{\"name\": $.customerName, \"items\": $.itemCount * 2}", "input_data.$": "$" }"""),
            ResultPath = "$.order",
            Next = "price",
        },
        ["price"] = new()
        {
            Type = StateType.Task,
            Resource = "transform://csharp",
            Parameters = new JObject
            {
                ["script"] = """
                    var order = (Newtonsoft.Json.Linq.JObject)input["order"];
                    double total = System.Math.Round(order["items"].Value<double>() * 19.99, 2);
                    order["total"] = total;
                    input["query"] = "INSERT INTO orders (customer_name, items, total) VALUES ('" + order["name"] + "', " + order["items"] + ", " + total.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
                    return input;
                """,
                ["input_data.$"] = "$",
            },
            Next = "record", // no ResultPath: the script's return value replaces the payload
        },
        ["record"] = new()
        {
            Type = StateType.Task,
            Resource = "sql://orders-db",
            Parameters = JObject.Parse("""{ "query.$": "$.query" }"""),
            ResultPath = "$.inserted",
            Next = "tableRead",
        },
        ["tableRead"] = new()
        {
            Type = StateType.Task,
            Resource = "sql://orders-db",
            Parameters = JObject.Parse("""{ "query": "SELECT id, customer_name, items, total FROM orders ORDER BY id" }"""),
            ResultPath = "$.orders",
            Next = "eavWrite",
        },
        ["eavWrite"] = new()
        {
            Type = StateType.Task,
            Resource = "eav://Order",
            Parameters = JObject.Parse("""{ "operation": "write", "values.$": "$.order" }"""),
            ResultPath = "$.eavWritten",
            Next = "eavRead",
        },
        ["eavRead"] = new()
        {
            Type = StateType.Task,
            Resource = "eav://Order",
            Parameters = JObject.Parse("{}"),
            ResultPath = "$.eavRows",
            Next = "approve",
        },
        ["approve"] = new()
        {
            Type = StateType.HumanTask,
            Task = JObject.Parse("""{ "title": "Approve order for dispatch", "assignee": "ops-team" }"""),
            ResultPath = "$.approval",
            Next = "done",
        },
        ["done"] = new() { Type = StateType.Succeed },
    },
};

// -- 5. Generation 1: run to the human task, then stop the host mid-execution -----
async Task<Execution> PollUntilAsync(StepFunctionService flows, string executionId, ExecutionStatus target)
{
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (DateTime.UtcNow < deadline)
    {
        var record = await flows.LoadStoredExecutionAsync(executionId);
        if (record is not null)
        {
            var exec = record.Execution;
            if (exec.Status == target || exec.Status is ExecutionStatus.Failed or ExecutionStatus.Aborted) return exec;
        }
        await Task.Delay(200);
    }
    throw new TimeoutException($"execution {executionId} did not reach {target} within 30s");
}

await using (var provider = services.BuildServiceProvider())
{
    var flows = provider.GetRequiredService<StepFunctionService>();
    await provider.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None);

    // Named rule: persisted to rules.json and live-registered with the SQL engine.
    var rules = provider.GetRequiredService<NamedRuleManager>();
    rules.Save(new NamedRule
    {
        Name = "pricing/standard",
        Kind = RuleKinds.Sql,
        Description = "Standard pricing gate (demo artifact)",
        Definition = JObject.Parse("""{ "expression": "{total} > 100", "requiredParameters": ["total"] }"""),
    });
    Console.WriteLine($"[rules] saved: {string.Join(", ", rules.List().Select(r => $"{r.Name} ({r.Kind})"))}");

    flows.RegisterStateMachine("order-flow", flow);

    var started = flows.StartExecution("order-flow", JToken.Parse("""{ "customerName": "acme-corp", "itemCount": 3 }"""));
    Console.WriteLine($"\n[gen1] execution {started.ExecutionId} started with input {{\"customerName\": \"acme-corp\", \"itemCount\": 3}}");

    var suspended = await PollUntilAsync(flows, started.ExecutionId, ExecutionStatus.Suspended);
    Console.WriteLine($"[gen1] status={suspended.Status} — flow paused before state '{suspended.CurrentState}', awaiting human input");

    if (suspended.Status is ExecutionStatus.Failed or ExecutionStatus.Aborted)
    {
        Console.WriteLine($"[gen1] FAILED in state '{suspended.CurrentState}': {suspended.ErrorMessage}");
        Environment.Exit(1);
    }
    // The human task record is what an approval UI would render.
    var task = (await provider.GetRequiredService<IHumanTaskStore>().ListAsync()).Single();
    Console.WriteLine($"[gen1] human task {task.TaskId}: title=\"{task.Title}\" assignee={task.Assignee}");

    // Stop the host while the flow is suspended — checkpoints stay on disk.
    await provider.GetServices<IHostedService>().Single().StopAsync(CancellationToken.None);
    Console.WriteLine("\n[gen1] host stopped mid-execution; checkpoint + human task persisted to disk\n");
}

// -- 6. Generation 2: fresh host, same paths — recovery + resume -------------------
await using (var provider = services.BuildServiceProvider())
{
    var flows = provider.GetRequiredService<StepFunctionService>();
    await provider.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None); // boot-time recovery

    Console.WriteLine("[gen2] stored executions after boot:");
    foreach (var s in await flows.ListStoredExecutionsAsync())
        Console.WriteLine($"  {s.ExecutionId} machine={s.StateMachineName} status={s.Status} state={s.CurrentState ?? "-"}");

    var rules = provider.GetRequiredService<NamedRuleManager>();
    Console.WriteLine($"\n[gen2] persisted rules after restart: {string.Join(", ", rules.List().Select(r => r.Name))}\n");

    var task = (await provider.GetRequiredService<IHumanTaskStore>().ListAsync()).Single(t => t.Status == HumanTaskStatus.Pending);
    Console.WriteLine($"[gen2] completing human task {task.TaskId} with approval...");
    await flows.CompleteHumanTaskAsync(task.TaskId, JToken.Parse("""{ "approved": true, "by": "ops-lead" }"""));

    var done = await PollUntilAsync(flows, task.ExecutionId, ExecutionStatus.Succeeded);
    Console.WriteLine($"\n[gen2] status={done.Status}, currentState={done.CurrentState}");
    Console.WriteLine($"[gen2] history: {string.Join(" -> ", done.History.Select(h => h.Type))}");
    Console.WriteLine("\n[gen2] final payload:");
    Console.WriteLine(done.Output.ToString(Formatting.Indented));
}

Console.WriteLine("\n+++ StepFlow.Asl demo complete +++");

/// <summary>Minimal IOptions&lt;T&gt; implementation (avoids the Options&lt;T&gt; type, which is unavailable in this dependency set).</summary>
internal sealed class FixedOptions : IOptions<FlowStateOptions>
{
    public FixedOptions(FlowStateOptions value) => Value = value;
    public FlowStateOptions Value { get; }
}
