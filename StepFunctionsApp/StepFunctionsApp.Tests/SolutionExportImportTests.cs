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
