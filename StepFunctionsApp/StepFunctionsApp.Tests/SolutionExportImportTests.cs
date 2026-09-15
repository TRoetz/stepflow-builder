using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities.DataSource;
using StepFlow.DataModel.Entities.MetaData;
using StepFlow.DynamicApi;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.DynamicApi;
using StepFunctionsApp.Rules;
using StepFunctionsApp.StepFunctions;
using StepFunctionsApp.Workspace;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Solution packaging end-to-end: export a workspace node's application (flows + forms +
    /// attribute domains + dynamic APIs + data-source schemas) from one isolated instance and
    /// import it onto a second fresh instance whose sql:// datasource file does not exist yet —
    /// then run an imported flow on the target to prove logical-datasource binding works there.
    /// </summary>
    public sealed class SolutionExportImportTests : IClassFixture<SolutionExportImportTests.SourceFactory>, IDisposable
    {
        private readonly SourceFactory _source;
        private readonly TargetFactory _target;
        private readonly string _node = "SolOrg/SolProj/SolSub";

        public SolutionExportImportTests(SourceFactory source)
        {
            _source = source;
            _target = new TargetFactory();
        }

        public void Dispose() => _target.Dispose();

        [Fact]
        public async Task Export_then_import_deploys_the_whole_application_to_a_fresh_instance()
        {
            var srcClient = _source.CreateClient();
            var tgtClient = _target.CreateClient();
            var workspace = _source.Services.GetRequiredService<WorkspaceStore>();

            // ── Arrange the source application: data db, flows, domain, form, api ──────────────
            var feesPath = Path.Combine(_source.Root, "data", "fees.db");
            Directory.CreateDirectory(Path.GetDirectoryName(feesPath)!);
            using (var conn = new SqliteConnection($"Data Source={feesPath};Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE fees (id INTEGER PRIMARY KEY, fee_type TEXT NOT NULL, year INTEGER NOT NULL, amount REAL, status TEXT);
                    CREATE TABLE fee_types (fee_type TEXT PRIMARY KEY, display_name TEXT, department TEXT);
                    INSERT INTO fee_types VALUES ('dog-license', 'Dog License Fee', 'Animal Control');
                    INSERT INTO fee_types VALUES ('liquor-license', 'Liquor License Fee', 'Liquor Licensing Board');
                    """;
                cmd.ExecuteNonQuery();
            }

            workspace.EnsureSubProject(_node);
            var queryFlow = new JObject
            {
                ["startAt"] = "q1",
                ["states"] = new JObject
                {
                    ["q1"] = new JObject
                    {
                        ["type"] = "Task",
                        ["resource"] = "sql://tds",
                        ["parameters"] = new JObject { ["query"] = "SELECT fee_type, department FROM fee_types ORDER BY fee_type" },
                        ["end"] = true
                    }
                }
            };
            var captureFlow = new JObject
            {
                ["startAt"] = "c1",
                ["states"] = new JObject
                {
                    ["c1"] = new JObject
                    {
                        ["type"] = "FormCapture",
                        ["task"] = new JObject { ["formId"] = "sol-form" },
                        ["completion"] = new JObject { ["Type"] = "form" }
                    }
                }
            };
            workspace.SaveFlow(_node, null, "sol-query", "Seeded lookup query", queryFlow.ToString(Formatting.None));
            workspace.SaveFlow(_node, null, "sol-capture", "Form capture demo", captureFlow.ToString(Formatting.None));

            var domains = _source.Services.GetRequiredService<IAttributeDomainStore>();
            domains.Save(new AttributeDomain
            {
                Version = "1",
                AttributeDomainName = "SolDomain",
                Description = "Solution test domain",
                IsCurrentVersion = true,
                Attributes = new System.Collections.Generic.List<EntityAttribute>
                {
                    new() { AttributeName = "note", DataType = StepFlow.DataModel.Entities.AttributeDataType.String, DisplayName = "Note" },
                    new() { AttributeName = "amount", DataType = StepFlow.DataModel.Entities.AttributeDataType.Number, DisplayName = "Amount" }
                }
            });

            var forms = _source.Services.GetRequiredService<IFormDefinitionStore>();
            forms.Save(new FormDefinition
            {
                FormId = "sol-form",
                Version = "1",
                Title = "Solution form",
                AttributeDomainName = "SolDomain",
                IsCurrentVersion = true,
                Page = JObject.Parse("""
                    {"Title":"Solution form","RootElements":[{"Id":1,"Type":"Section","Label":"Details","Children":[
                      {"Id":2,"Type":"TextBox","Attribute":"note","Label":"Note"},
                      {"Id":3,"Type":"TextBox","Attribute":"amount","Label":"Amount"}]}]}
                    """)
            });

            // Data-exchange profile filed under the node (round-trips through the package).
            var dxProfiles = _source.Services.GetRequiredService<DataExchangeProfileStore>();
            var solDxJson = """
                {
                  "DataExchangeProfileName": "Sol DX Import",
                  "ProfileId": "sol-dx",
                  "IsActive": true,
                  "DataSource": { "MediumType": 3, "MediumConfigurationJson": "{\"filePath\": \"C:/temp/sol-dx.csv\"}" },
                  "Pipeline": { "PipelineName": "Sol DX Pipeline", "PipelineStages": [ { "StageType": 0, "ExecutionOrder": 1 } ] }
                }
                """;
            dxProfiles.Save(JsonConvert.DeserializeObject<DataExchangeProfile>(solDxJson)!, _node);

            var apis = _source.Services.GetRequiredService<IDynamicApiStore>();
            apis.Save(new DynamicApiDefinition
            {
                Id = "Sol-Test-API",
                Name = "Solution Test API",
                NodePath = _node,
                BasePath = "/sol-test",
                IsPublished = true,
                Operations = new System.Collections.Generic.List<DynamicApiOperation>
                {
                    new() { Method = "GET", Path = "", HandlerType = "flow", FlowId = "sol-query" }
                }
            });

            // ── Export from the source instance ────────────────────────────────────────────────
            var exportRes = await srcClient.GetAsync($"/api/solutions/export?nodePath={Uri.EscapeDataString(_node)}&seedTables=fee_types");
            var exportText = await exportRes.Content.ReadAsStringAsync();
            Assert.True(exportRes.StatusCode == HttpStatusCode.OK, $"export failed: {exportText}");
            var package = JObject.Parse(exportText);

            Assert.Equal("stepflow-solution", (string)package["format"]);
            Assert.Equal(_node, (string)package["manifest"]?["sourceNodePath"]);
            var flowNames = package["flows"]!.Select(f => (string?)f!["name"]).ToList();
            Assert.Contains("sol-query", flowNames);
            Assert.Contains("sol-capture", flowNames);
            Assert.Equal("sol-form", (string)package["forms"]![0]!["formId"]);
            Assert.Equal("SolDomain", (string)package["forms"]![0]!["attributeDomainName"]);
            var domain = package["attributeDomains"]!.Single();
            Assert.Equal("SolDomain", (string?)domain?["name"]);
            Assert.Equal(2, domain?["attributes"]?.Count() ?? 0);
            Assert.Equal("Sol-Test-API", (string)package["dynamicApis"]![0]!["id"]);
            var dx = package["dataExchangeProfiles"]!.Single();
            Assert.Equal("sol-dx", (string?)dx?["id"]);
            Assert.Equal("Sol DX Import", (string?)dx?["document"]?["DataExchangeProfileName"]);
            var ds = package["dataSources"]!.Single();
            Assert.Equal("tds", (string?)ds?["name"]);
            var allSql = string.Join("\n", ds!["migrations"]!.Select(m => (string)m!));
            Assert.Contains("CREATE TABLE IF NOT EXISTS fees", allSql);
            Assert.Contains("CREATE TABLE IF NOT EXISTS fee_types", allSql);
            Assert.Contains("INSERT OR IGNORE INTO \"fee_types\"", allSql);

            // ── Import onto the fresh target instance (its tds file does not exist yet) ────────
            var importBody = new JObject { ["package"] = package, ["targetNodePath"] = _node };
            var importRes = await tgtClient.PostAsync("/api/solutions/import",
                new StringContent(JsonConvert.SerializeObject(importBody), Encoding.UTF8, "application/json"));
            var importText = await importRes.Content.ReadAsStringAsync();
            Assert.True(importRes.StatusCode == HttpStatusCode.OK, $"import failed: {importText}");
            var report = JObject.Parse(importText);

            Assert.Equal(2, report["flows"]!.Count());
            Assert.All(report["flows"]!.Cast<JObject>(), f => Assert.True((bool)f!["created"]!));
            Assert.Single(report["forms"]!);
            Assert.Single(report["attributeDomains"]!);
            Assert.Single(report["dynamicApis"]!);
            var tgtDx = report["dataExchangeProfiles"]!.Single();
            Assert.Equal("sol-dx", (string?)tgtDx?["id"]);
            var tgtDs = report["dataSources"]!.Single();
            var expectedTargetDb = Path.Combine(_target.Root, "data", "fees.db");
            Assert.Equal(Path.GetFullPath(expectedTargetDb), (string?)tgtDs?["path"]);
            Assert.True(File.Exists(expectedTargetDb)); // created by the import

            // ── The imported application actually works on the target ─────────────────────────
            var tgtWorkspace = _target.Services.GetRequiredService<WorkspaceStore>();
            Assert.Contains(tgtWorkspace.ListFlows(_node), f => f.Meta.Name == "sol-query");
            Assert.NotNull(_target.Services.GetRequiredService<IFormDefinitionStore>().Get("sol-form"));
            Assert.NotNull(_target.Services.GetRequiredService<IAttributeDomainStore>().GetByName("SolDomain").domain);
            Assert.Single(_target.Services.GetRequiredService<IDynamicApiStore>().GetAll(_node));

            // The data-exchange profile was re-filed under the target node and is loadable there.
            var tgtDxProfile = _target.Services.GetRequiredService<DataExchangeProfileStore>().Get("sol-dx");
            Assert.NotNull(tgtDxProfile);
            Assert.Equal("Sol DX Import", tgtDxProfile!.DataExchangeProfileName);
            var tgtDxFile = Path.Combine(_target.Root, "workspace-data", _node.Replace('/', Path.DirectorySeparatorChar), "data-exchange", "sol-dx", "profile.json");
            Assert.True(File.Exists(tgtDxFile), $"expected profile file at {tgtDxFile}");

            // Run the imported flow: sql://tds must resolve to the target's freshly created database.
            var runRes = await tgtClient.PostAsync("/api/flows/execute-sync/sol-query",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, runRes.StatusCode);
            var execText = await runRes.Content.ReadAsStringAsync();
            var execution = JObject.Parse(execText);
            Assert.True((string?)execution?["status"] == "Succeeded",
                $"flow failed: errorCode={(string?)execution?["errorCode"]} errorMessage={(string?)execution?["errorMessage"]} body={execText}");
            var rows = execution["output"]?["rows"];
            Assert.NotNull(rows);
            Assert.Contains(rows!.Select(r => (string?)r!["fee_type"]), v => v == "dog-license");

            // ── Re-import is an idempotent redeploy ────────────────────────────────────────────
            var reimportRes = await tgtClient.PostAsync("/api/solutions/import",
                new StringContent(JsonConvert.SerializeObject(importBody), Encoding.UTF8, "application/json"));
            var reimportText = await reimportRes.Content.ReadAsStringAsync();
            Assert.True(reimportRes.StatusCode == HttpStatusCode.OK, $"re-import failed: {reimportText}");
            var reReport = JObject.Parse(reimportText);
            Assert.All(reReport["flows"]!.Cast<JObject>(), f => Assert.False((bool)f!["created"]!)); // updated in place
        }

        [Fact]
        public async Task Canvas_layout_round_trips_through_the_solution_package()
        {
            var node = "CanvasOrg/CanvasProj/CanvasSub";
            var srcClient = _source.CreateClient();
            var tgtClient = _target.CreateClient();
            var workspace = _source.Services.GetRequiredService<WorkspaceStore>();

            // A flow with designer canvas metadata (labels + positions).
            var flow = new JObject
            {
                ["startAt"] = "s1",
                ["states"] = new JObject
                {
                    ["s1"] = new JObject { ["type"] = "Task", ["resource"] = "internal://noop", ["end"] = false },
                    ["s2"] = new JObject { ["type"] = "Task", ["resource"] = "internal://noop", ["end"] = true }
                },
                ["canvas"] = new JObject
                {
                    ["nodes"] = new JObject
                    {
                        ["s1"] = new JObject { ["label"] = "Start", ["position"] = new JObject { ["x"] = 10, ["y"] = 20 } },
                        ["s2"] = new JObject { ["label"] = "Done", ["position"] = new JObject { ["x"] = 300, ["y"] = 40 } }
                    }
                }
            };
            workspace.EnsureSubProject(node);
            workspace.SaveFlow(node, null, "sol-canvas-flow", "Canvas round-trip demo", flow.ToString(Formatting.None));

            // Export carries the canvas.
            var exportRes = await srcClient.GetAsync($"/api/solutions/export?nodePath={Uri.EscapeDataString(node)}");
            Assert.Equal(HttpStatusCode.OK, exportRes.StatusCode);
            var package = JObject.Parse(await exportRes.Content.ReadAsStringAsync());
            var exportedFlow = package["flows"]!.Single(f => (string?)f!["name"] == "sol-canvas-flow")!;
            Assert.Equal(10, (int)exportedFlow!["canvas"]!["nodes"]!["s1"]!["position"]!["x"]!);
            Assert.Equal("Start", (string?)exportedFlow["canvas"]?["nodes"]?["s1"]?["label"]);

            // Import onto the target — the persisted flow.json keeps its layout.
            var importRes = await tgtClient.PostAsync("/api/solutions/import",
                new StringContent(JsonConvert.SerializeObject(new JObject { ["package"] = package, ["targetNodePath"] = node }), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, importRes.StatusCode);

            var tgtWorkspace = _target.Services.GetRequiredService<WorkspaceStore>();
            var (flowId, _) = tgtWorkspace.ListFlows(node).Single(f => f.Meta.Name == "sol-canvas-flow");
            var importedDoc = JObject.Parse(tgtWorkspace.LoadFlowDefinition(node, flowId)!);
            Assert.Equal(300, (int)importedDoc["canvas"]!["nodes"]!["s2"]!["position"]!["x"]!);
            Assert.Equal("Done", (string?)importedDoc["canvas"]?["nodes"]?["s2"]?["label"]);
        }

        [Fact]
        public async Task Eav_datasets_round_trip_and_reimport_is_idempotent()
        {
            var node = "EavOrg/EavProj/EavSub";
            var srcClient = _source.CreateClient();
            var tgtClient = _target.CreateClient();

            // Source: an entity contract + two captured rows, referenced by a flow.
            var registry = _source.Services.GetRequiredService<EavRegistryService>();
            registry.RegisterEntity(new EavEntityDefinition
            {
                EntityName = "SolInvoice",
                Description = "Solution test invoice dataset",
                Attributes = new System.Collections.Generic.List<EavAttributeDefinition>
                {
                    new() { AttributeName = "invoiceNumber", DataType = "string", IsRequired = true },
                    new() { AttributeName = "totalAmount", DataType = "number" }
                }
            });
            var rows = _source.Services.GetRequiredService<EavRowStore>();
            rows.AppendRow("SolInvoice", new EavRow { RowKeyId = "sol-inv-r1", Values = JObject.Parse("""{"invoiceNumber":"INV-1","totalAmount":100}""") });
            rows.AppendRow("SolInvoice", new EavRow { RowKeyId = "sol-inv-r2", Values = JObject.Parse("""{"invoiceNumber":"INV-2","totalAmount":250.5}""") });

            var workspace = _source.Services.GetRequiredService<WorkspaceStore>();
            workspace.EnsureSubProject(node);
            workspace.SaveFlow(node, null, "sol-eav-flow", "EAV reference demo",
                new JObject
                {
                    ["startAt"] = "e1",
                    ["states"] = new JObject
                    {
                        ["e1"] = new JObject { ["type"] = "Task", ["resource"] = "eav://SolInvoice", ["parameters"] = new JObject { ["operation"] = "read" }, ["end"] = true }
                    }
                }.ToString(Formatting.None));

            // Export carries the entity contract and the row dump.
            var exportRes = await srcClient.GetAsync($"/api/solutions/export?nodePath={Uri.EscapeDataString(node)}");
            Assert.Equal(HttpStatusCode.OK, exportRes.StatusCode);
            var package = JObject.Parse(await exportRes.Content.ReadAsStringAsync());

            var entity = package["eavEntities"]!.Single(e => (string?)e!["name"] == "SolInvoice")!;
            Assert.Equal(2, entity!["attributes"]!.Count());
            Assert.Contains(entity["attributes"]!.Cast<JObject>(), a => (string?)a!["attributeName"] == "invoiceNumber" && (bool)a!["isRequired"]!);
            var rowSet = package["eavRows"]!.Single(s => (string?)s!["domain"] == "SolInvoice")!;
            Assert.Equal(2, rowSet!["rows"]!.Count());
            Assert.Contains(rowSet["rows"]!.Cast<JObject>(), r => (string?)r!["rowKeyId"] == "sol-inv-r1");

            // Import onto the fresh target.
            var importBody = new JObject { ["package"] = package, ["targetNodePath"] = node };
            var importRes = await tgtClient.PostAsync("/api/solutions/import",
                new StringContent(JsonConvert.SerializeObject(importBody), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, importRes.StatusCode);
            var report = JObject.Parse(await importRes.Content.ReadAsStringAsync());

            Assert.Single(report["eavEntities"]!);
            Assert.Equal(2, (int)report["eavRows"]![0]!["added"]!);

            var tgtRegistry = _target.Services.GetRequiredService<EavRegistryService>();
            var tgtEntity = tgtRegistry.GetEntity("SolInvoice");
            Assert.NotNull(tgtEntity);
            Assert.Equal(2, tgtEntity!.Attributes.Count);
            var tgtRows = _target.Services.GetRequiredService<EavRowStore>().ListRows("SolInvoice");
            Assert.Equal(2, tgtRows.Count);
            Assert.Contains(tgtRows, r => r.RowKeyId == "sol-inv-r1" && (string?)r.Values["invoiceNumber"] == "INV-1");

            // Re-import: rows are keyed by rowKeyId — nothing is duplicated.
            var reimportRes = await tgtClient.PostAsync("/api/solutions/import",
                new StringContent(JsonConvert.SerializeObject(importBody), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, reimportRes.StatusCode);
            var reReport = JObject.Parse(await reimportRes.Content.ReadAsStringAsync());
            Assert.Equal(0, (int)reReport["eavRows"]![0]!["added"]!);
            Assert.Equal(2, (int)reReport["eavRows"]![0]!["skippedExisting"]!);
            Assert.Equal(2, _target.Services.GetRequiredService<EavRowStore>().ListRows("SolInvoice").Count);
        }

        [Fact]
        public async Task Rules_are_separate_artifacts_and_survive_redeploy()
        {
            var node = "RuleOrg/RuleProj/RuleSub";
            var srcClient = _source.CreateClient();
            var tgtClient = _target.CreateClient();

            // A persisted standalone rule (sql kind — engine-backed via rule://).
            var rules = _source.Services.GetRequiredService<NamedRuleManager>();
            rules.Save(new NamedRule
            {
                Name = "SolFeeCheck",
                Kind = RuleKinds.Sql,
                Description = "Fees above 100 need review.",
                Definition = JObject.Parse("""{"expression":"{amount} > 100","requiredParameters":["amount"]}""")
            });

            var workspace = _source.Services.GetRequiredService<WorkspaceStore>();
            workspace.EnsureSubProject(node);

            // Flow A: executes the persisted rule via rule://SolFeeCheck.
            workspace.SaveFlow(node, null, "sol-fee-check", "Rule-backed flow",
                new JObject
                {
                    ["startAt"] = "r1",
                    ["states"] = new JObject
                    {
                        ["r1"] = new JObject { ["type"] = "Task", ["resource"] = "rule://SolFeeCheck", ["parameters"] = new JObject { ["amount"] = 500 }, ["end"] = true }
                    }
                }.ToString(Formatting.None));

            // Flow B: inline Choice + JSONata logic that export must lift into rule artifacts.
            workspace.SaveFlow(node, null, "sol-choice-flow", "Choice + transform demo",
                new JObject
                {
                    ["startAt"] = "c1",
                    ["states"] = new JObject
                    {
                        ["c1"] = new JObject
                        {
                            ["type"] = "Choice",
                            ["choices"] = new JArray
                            {
                                new JObject { ["expression"] = "$amount > 0", ["next"] = "t1" },
                                new JObject { ["expression"] = "true", ["next"] = "t1" }
                            }
                        },
                        ["t1"] = new JObject
                        {
                            ["type"] = "Task",
                            ["resource"] = "transform://jsonata",
                            ["parameters"] = new JObject { ["expression"] = "$amount * 2" },
                            ["end"] = true
                        }
                    }
                }.ToString(Formatting.None));

            // Export: the package's rules section is a standalone catalog.
            var exportRes = await srcClient.GetAsync($"/api/solutions/export?nodePath={Uri.EscapeDataString(node)}");
            Assert.Equal(HttpStatusCode.OK, exportRes.StatusCode);
            var package = JObject.Parse(await exportRes.Content.ReadAsStringAsync());

            var ruleNames = package["rules"]!.Select(r => (string?)r!["name"]).ToList();
            Assert.Contains("SolFeeCheck", ruleNames);
            Assert.Contains("sol-choice-flow/c1", ruleNames);   // Choice logic lifted out of the flow
            Assert.Contains("sol-choice-flow/t1", ruleNames);   // JSONata transform lifted out of the flow

            var sqlRule = package["rules"]!.Single(r => (string?)r!["name"] == "SolFeeCheck")!;
            Assert.Equal(RuleKinds.Sql, (string?)sqlRule!["kind"]);
            Assert.Equal("{amount} > 100", (string?)sqlRule["definition"]?["expression"]);
            var choiceRule = package["rules"]!.Single(r => (string?)r!["name"] == "sol-choice-flow/c1")!;
            Assert.Equal(RuleKinds.Choice, (string?)choiceRule!["kind"]);
            Assert.Equal(2, choiceRule!["definition"]!["conditions"]!.Count());

            // Import onto the fresh target: rules land in its catalog and engines.
            var importRes = await tgtClient.PostAsync("/api/solutions/import",
                new StringContent(JsonConvert.SerializeObject(new JObject { ["package"] = package, ["targetNodePath"] = node }), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, importRes.StatusCode);
            var report = JObject.Parse(await importRes.Content.ReadAsStringAsync());
            Assert.Equal(3, report["rules"]!.Count());

            var tgtRules = _target.Services.GetRequiredService<NamedRuleManager>();
            Assert.NotNull(tgtRules.Get("SolFeeCheck"));
            Assert.NotNull(tgtRules.Get("sol-choice-flow/c1"));
            Assert.True(File.Exists(Path.Combine(_target.Root, "rules.json"))); // persisted artifact on disk

            // The imported rule is engine-registered: running the flow executes it on the target.
            var runRes = await tgtClient.PostAsync("/api/flows/execute-sync/sol-fee-check",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, runRes.StatusCode);
            var execution = JObject.Parse(await runRes.Content.ReadAsStringAsync());
            Assert.True((string?)execution?["status"] == "Succeeded",
                $"flow failed: errorCode={(string?)execution?["errorCode"]} errorMessage={(string?)execution?["errorMessage"]}");
            // RuleResultEx serializes with PascalCase keys (JObject.FromObject default).
            var ruleOut = execution["output"]!;
            Assert.True((bool)ruleOut!["Status"]!, $"rule {{amount}} > 100 should pass for amount=500; output={ruleOut}");
        }

        /// <summary>Isolated source instance: its own workspace, stepflow_data.db and tds datasource binding.</summary>
        public sealed class SourceFactory : WebApplicationFactory<Program>
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "stepflow-solution-tests-src", Guid.NewGuid().ToString("N"));

            protected override void ConfigureWebHost(IWebHostBuilder builder) =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [FlowStateOptions.SectionName + ":DiskPath"] = Path.Combine(Root, "flow-state"),
                        [FormDataOptions.SectionName + ":DatabasePath"] = Path.Combine(Root, "stepflow_data.db"),
                        [WorkspaceOptions.SectionName + ":RootDirectory"] = Path.Combine(Root, "workspace-data"),
                        ["SqlDataSources:tds"] = Path.Combine(Root, "data", "fees.db"),
                        ["Eav:RegistryPath"] = Path.Combine(Root, "eav_registry.json"),
                        ["Eav:DataDirectory"] = Path.Combine(Root, "eav-data"),
                        ["Rules:Path"] = Path.Combine(Root, "rules.json"),
                    }));

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
            }
        }

        /// <summary>Fresh target instance — simulates prod: no data file yet, same logical binding name.</summary>
        public sealed class TargetFactory : WebApplicationFactory<Program>, IDisposable
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "stepflow-solution-tests-tgt", Guid.NewGuid().ToString("N"));

            protected override void ConfigureWebHost(IWebHostBuilder builder) =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [FlowStateOptions.SectionName + ":DiskPath"] = Path.Combine(Root, "flow-state"),
                        [FormDataOptions.SectionName + ":DatabasePath"] = Path.Combine(Root, "stepflow_data.db"),
                        [WorkspaceOptions.SectionName + ":RootDirectory"] = Path.Combine(Root, "workspace-data"),
                        ["SqlDataSources:tds"] = Path.Combine(Root, "data", "fees.db"),
                        ["Eav:RegistryPath"] = Path.Combine(Root, "eav_registry.json"),
                        ["Eav:DataDirectory"] = Path.Combine(Root, "eav-data"),
                        ["Rules:Path"] = Path.Combine(Root, "rules.json"),
                    }));

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
