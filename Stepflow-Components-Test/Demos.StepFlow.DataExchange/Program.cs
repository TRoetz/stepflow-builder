using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.DataSource;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.StepFunctions;
using Action = StepFlow.DataModel.Entities.DataSource.Action;

Console.WriteLine("+++ StepFlow.DataExchange demo +++");
Console.WriteLine("CSV ingestion -> Logic (validation + calculation) -> Transformation (schema map) -> file dispatch\n");

var ws = Path.Combine(Path.GetTempPath(), "stepflow-demo", "dataexchange");
if (Directory.Exists(ws)) Directory.Delete(ws, true);
Directory.CreateDirectory(Path.Combine(ws, "out"));

// ── 1. Source data: a CSV with one row that will fail validation ─────────────
var csvPath = Path.Combine(ws, "orders.csv");
File.WriteAllText(csvPath, """
id,customer,region,amount
101,alice,north,250
102,bob,south,-50
103,carol,east,480
""");

// ── 2. Build the profile in code (same model the JSON/SQLite stores persist) ─
var profile = new DataExchangeProfile
{
    ProfileId = "orders-import",
    DataExchangeProfileName = "Orders Import",
    IsActive = true,
    DataSource = new DataSource
    {
        DataSourceName = "orders-csv",
        MediumType = DataSourceMediumType.File
    },
    Pipeline = new Pipeline
    {
        PipelineName = "import-pipeline",
        PipelineStages = new List<PipelineStage>
        {
            // Stage 1: Logic - reject bad rows, derive a discounted price.
            new()
            {
                ExecutionOrder = 1,
                PipelineStageActions = new List<PipelineStageAction>
                {
                    new()
                    {
                        ExecutionOrder = 1,
                        Action = new Action
                        {
                            ActionName = "validate-and-enrich",
                            Type = ActionType.Logic,
                            ActionRules = new List<ActionRule>
                            {
                                new()
                                {
                                    Rule = new Rule
                                    {
                                        RuleName = "positive-amount",
                                        Type = RuleType.Validation,
                                        Expression = "{amount} > 0",
                                        DefaultFailureMessage = "amount must be positive"
                                    }
                                },
                                new()
                                {
                                    OutputTargetAttributeName = "discounted",
                                    Rule = new Rule
                                    {
                                        RuleName = "discounted-price",
                                        Type = RuleType.Calculation,
                                        Expression = "round({amount} * 1.2, 2)"
                                    }
                                }
                            }
                        }
                    }
                }
            },

            // Stage 2: Transformation - map source columns onto the target schema.
            new()
            {
                ExecutionOrder = 2,
                PipelineStageActions = new List<PipelineStageAction>
                {
                    new()
                    {
                        ExecutionOrder = 1,
                        Action = new Action
                        {
                            ActionName = "map-to-target",
                            Type = ActionType.Transformation,
                            SchemaMap = new ActionSchemaMap
                            {
                                AttributeMappings = new List<SchemaMap>
                                {
                                    new()
                                    {
                                        TargetAttribute = new EntityAttribute { AttributeName = "order_id" },
                                        SourceAttributes = new List<EntityAttribute> { new() { AttributeName = "id" } },
                                        TransformType = TransformType.DirectCopy
                                    },
                                    new()
                                    {
                                        TargetAttribute = new EntityAttribute { AttributeName = "customer_name" },
                                        SourceAttributes = new List<EntityAttribute> { new() { AttributeName = "customer" } },
                                        TransformType = TransformType.ToUpper
                                    },
                                    new()
                                    {
                                        TargetAttribute = new EntityAttribute { AttributeName = "region_code" },
                                        SourceAttributes = new List<EntityAttribute> { new() { AttributeName = "region" } },
                                        TransformType = TransformType.ToUpper
                                    },
                                    new()
                                    {
                                        TargetAttribute = new EntityAttribute { AttributeName = "total" },
                                        SourceAttributes = new List<EntityAttribute> { new() { AttributeName = "discounted" } },
                                        TransformType = TransformType.DirectCopy
                                    }
                                }
                            }
                        }
                    }
                }
            },

            // Stage 3: Dispatch - write the surviving rows to CSV and JSON files.
            new()
            {
                ExecutionOrder = 3,
                PipelineStageActions = new List<PipelineStageAction>
                {
                    new()
                    {
                        ExecutionOrder = 1,
                        Action = new Action
                        {
                            ActionName = "dispatch-csv",
                            Type = ActionType.Dispatch,
                            Endpoint = new ActionEndpoint
                            {
                                ActionEndpointURL = $"file://{Path.Combine(ws, "out", "orders.csv")}"
                            },
                            Parameters = new Dictionary<string, string> { ["OutputFormat"] = "csv" }
                        }
                    },
                    new()
                    {
                        ExecutionOrder = 2,
                        Action = new Action
                        {
                            ActionName = "dispatch-json",
                            Type = ActionType.Dispatch,
                            Endpoint = new ActionEndpoint
                            {
                                ActionEndpointURL = $"file://{Path.Combine(ws, "out", "orders.json")}"
                            },
                            Parameters = new Dictionary<string, string> { ["OutputFormat"] = "json" }
                        }
                    }
                }
            }
        }
    }
};

// ── 3. Execute the profile against the CSV file ──────────────────────────────
var store = new DataExchangeProfileStore(ws, Path.Combine(ws, "profiles"));
using var duck = new DuckDbTransformService(NullLogger<DuckDbTransformService>.Instance);
var executor = new DataExchangeExecutor(store, duck, new HttpClientFactory(), NullLogger<DataExchangeExecutor>.Instance);

Console.WriteLine($"[run] profile={profile.ProfileId}, input filePath={csvPath}");
var report = await executor.ExecuteProfileAsync(profile, new JObject { ["filePath"] = csvPath }, CancellationToken.None);

// ── 4. Print the execution report ────────────────────────────────────────────
Console.WriteLine($"\n[report] success={report["success"]} rowsIn={report["rowsIn"]} rowsOut={report["rowsOut"]} rejectedCount={report["rejectedCount"]}\n");

if (report["rejected"] is JArray rejections)
{
    Console.WriteLine("[rejected]");
    foreach (var r in rejections)
        Console.WriteLine($"  row {r["rowIndex"]}: {r["reason"]} -> {r["row"].ToString(Formatting.None)}\n");
}

if (report["stages"] is JArray stages)
{
    Console.WriteLine("[stages]");
    foreach (var s in stages)
        foreach (var a in (JArray)s["actions"])
            Console.WriteLine($"  stage {s["order"]} ({s["stageType"]}) action={a["name"]} [{a["type"]}]: " + string.Join("; ", ((JArray)a["messages"]).Select(m => m.ToString())));
    Console.WriteLine();
}

if (report["dispatched"] is JArray dispatched)
{
    Console.WriteLine("[dispatched]");
    foreach (var d in dispatched)
        Console.WriteLine($"  {d["endpoint"]} method={d["method"]} rows={d["rows"]} ok={d["ok"]} failed={d["failed"]}");
}

// ── 5. Show what actually landed on disk ─────────────────────────────────────
Console.WriteLine($"\n[out] {Path.Combine(ws, "out", "orders.csv")}:");
foreach (var line in File.ReadAllLines(Path.Combine(ws, "out", "orders.csv")))
    Console.WriteLine($"  {line}");

Console.WriteLine($"\n[out] {Path.Combine(ws, "out", "orders.json")}:");
Console.WriteLine(File.ReadAllText(Path.Combine(ws, "out", "orders.json")).Trim());

Console.WriteLine("\nDataExchange demo complete.");

// Trivial IHttpClientFactory so the executor's HTTP dispatch path is constructible.
sealed class HttpClientFactory : System.Net.Http.IHttpClientFactory
{
    public HttpClient CreateClient(string name = "") => new();
}
