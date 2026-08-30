using System.Data;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities.DataSource;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests;

/// <summary>
/// In-process end-to-end test of the Data Exchange pipeline: customer CSV (external schema) ->
/// map to internal attributes -> FX enrichment via lookup API on a local fake host -> dispatch of the
/// enriched set (CSV) and the filtered North-region subset (JSON). Hermetic: fixtures are embedded,
/// all file paths are rewritten to an isolated temp dir, and the fake-API base URL is rewritten to a
/// local Kestrel socket - no external services or machine-specific files required.
/// </summary>
public sealed class DataExchangePipelineTests : IClassFixture<DataExchangePipelineTests.Factory>, IClassFixture<DataExchangePipelineTests.FakeFactory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        // Isolate durable flow-state so startup recovery never loads checkpoints from other runs.
        private readonly string _flowStateDir = Path.Combine(Path.GetTempPath(), "stepflow-tests", Guid.NewGuid().ToString("N"));

        // Point the Data Exchange subsystem (profile store, monitor inbox, dispatch output) at isolated temp dirs.
        private readonly string _dxRoot = Path.Combine(Path.GetTempPath(), "dataexchange-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [FlowStateOptions.SectionName + ":DiskPath"] = _flowStateDir,
                    [DataExchangeOptions.SectionName + ":ProfilesDirectory"] = Path.Combine(_dxRoot, "profiles"),
                    [DataExchangeOptions.SectionName + ":InboxDirectory"] = Path.Combine(_dxRoot, "inbox"),
                    [DataExchangeOptions.SectionName + ":OutputDirectory"] = Path.Combine(_dxRoot, "output"),
                    // Keep the file monitor quiet during tests.
                    [DataExchangeOptions.SectionName + ":PollIntervalSeconds"] = "3600",
                }));

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { Directory.Delete(_flowStateDir, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(_dxRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Fake test API host (Stepflow-Builder-Tests) on a real Kestrel socket - the profile's FX lookup endpoint lives here.</summary>
    public sealed class FakeFactory : WebApplicationFactory<Stepflow_Builder_Tests.Program>
    {
        private bool _started;

        public string BaseUrl { get; private set; } = "";

        /// <summary>Binds an ephemeral loopback port and captures it. Idempotent - safe to call from every test constructor.</summary>
        public void StartKestrel()
        {
            if (_started) return;
            _started = true;
            UseKestrel(o => o.Listen(IPAddress.Loopback, 0));
            _ = CreateClient(); // start the server so its address is available
            var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            BaseUrl = (addresses.FirstOrDefault(a => a.Contains("127.0.0.1")) ?? addresses.First()).TrimEnd('/');
        }
    }

    /// <summary>Customer file in the external schema, as delivered by the customer.</summary>
    private const string CsvFixture =
        "OrderID,CustomerCode,Region,Amount,CurrencyCode\n" +
        "ORD-1001,cust-a,North,100.00,USD\n" +
        "ORD-1002,cust-b,South,50.00,EUR\n" +
        "ORD-1003,cust-c,North,25.50,USD";

    /// <summary>Profile under test: __WORKDIR__ is rewritten to the isolated temp dir; the FX lookup URL is rewritten to the fake host.</summary>
    private const string ProfileTemplate = """
        {
          "DataExchangeProfileName": "Customer Orders Import",
          "ProfileId": "customer-orders-import",
          "IsActive": true,
          "DataSource": {
            "DataSourceName": "cust_orders.csv",
            "MediumType": "File",
            "MediumConfigurationJson": "{\"filePath\": \"__WORKDIR__/cust_orders.csv\"}"
          },
          "Pipeline": {
            "PipelineName": "CustomerOrdersToInternal",
            "Description": "Map customer CSV to internal schema, enrich with NZD FX rate via lookup API, dispatch enriched set (CSV) and North-region subset (JSON).",
            "PipelineStages": [
              {
                "StageType": "DataTreatment",
                "ExecutionOrder": 1,
                "PipelineStageActions": [
                  {
                    "ExecutionOrder": 1,
                    "Action": {
                      "ActionName": "MapToInternalSchema",
                      "Type": "Transformation",
                      "SchemaMap": {
                        "AttributeMappings": [
                          {
                            "TargetAttribute": { "AttributeName": "OrderNumber" },
                            "SourceAttributes": [{ "AttributeName": "OrderID" }],
                            "TransformType": "DirectCopy",
                            "MergeStrategy": "OverwriteExisting"
                          },
                          {
                            "TargetAttribute": { "AttributeName": "CustomerRef" },
                            "SourceAttributes": [{ "AttributeName": "CustomerCode" }],
                            "TransformType": "ToUpper",
                            "MergeStrategy": "OverwriteExisting"
                          },
                          {
                            "TargetAttribute": { "AttributeName": "RegionName" },
                            "SourceAttributes": [{ "AttributeName": "Region" }],
                            "TransformType": "DirectCopy",
                            "MergeStrategy": "OverwriteExisting"
                          },
                          {
                            "TargetAttribute": { "AttributeName": "AmountUSD" },
                            "SourceAttributes": [{ "AttributeName": "Amount" }],
                            "TransformType": "DirectCopy",
                            "MergeStrategy": "OverwriteExisting"
                          },
                          {
                            "TargetAttribute": { "AttributeName": "Currency" },
                            "SourceAttributes": [{ "AttributeName": "CurrencyCode" }],
                            "TransformType": "DirectCopy",
                            "MergeStrategy": "OverwriteExisting"
                          }
                        ]
                      }
                    }
                  }
                ]
              },
              {
                "StageType": "DataTreatment",
                "ExecutionOrder": 2,
                "PipelineStageActions": [
                  {
                    "ExecutionOrder": 1,
                    "Action": {
                      "ActionName": "EnrichWithNzdRate",
                      "Type": "EnrichmentLookup",
                      "OutputParameterName": "AmountNZD",
                      "Lookup": {
                        "LookupName": "fx-rate-nzd",
                        "LookupEndpoint": "http://localhost:5095/api/fake/exchange-rate?from={Currency}&to=NZD&amt={AmountUSD}",
                        "Type": "Api",
                        "ValueFieldToReturn": "convertedAmount"
                      }
                    }
                  }
                ]
              },
              {
                "StageType": "PreRouting",
                "ExecutionOrder": 3,
                "PipelineStageActions": [
                  {
                    "ExecutionOrder": 1,
                    "Action": {
                      "ActionName": "DispatchAllEnrichedCsv",
                      "Type": "Dispatch",
                      "Endpoint": { "ActionEndpointURL": "file://__WORKDIR__/out/enriched-orders.csv" },
                      "Parameters": { "OutputFormat": "csv" }
                    }
                  },
                  {
                    "ExecutionOrder": 2,
                    "Action": {
                      "ActionName": "DispatchNorthOnlyJson",
                      "Type": "Dispatch",
                      "Endpoint": { "ActionEndpointURL": "file://__WORKDIR__/out/north-orders.json" },
                      "Parameters": { "Filter": "\"RegionName\" = 'North'", "OutputFormat": "json" }
                    }
                  }
                ]
              }
            ]
          }
        }
        """;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _fxClient;

    public DataExchangePipelineTests(Factory factory, FakeFactory fakeFactory)
    {
        _factory = factory;
        if (string.IsNullOrEmpty(fakeFactory.BaseUrl)) fakeFactory.StartKestrel();
        _fxClient = new HttpClient { BaseAddress = new Uri(fakeFactory.BaseUrl) };
    }

    [Fact]
    public async Task CustomerOrdersImport_FullPipeline_MapsEnrichesAndDispatches()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "dx-pipeline-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            File.WriteAllText(Path.Combine(workDir, "cust_orders.csv"), CsvFixture);

            var profileJson = ProfileTemplate
                .Replace("__WORKDIR__", workDir.Replace('\\', '/'))
                .Replace("http://localhost:5095", _fxClient.BaseAddress!.ToString().TrimEnd('/'));
            var profile = JsonConvert.DeserializeObject<DataExchangeProfile>(profileJson)!;

            var executor = _factory.Services.GetRequiredService<DataExchangeExecutor>();
            var result = await executor.ExecuteProfileAsync(profile, new JObject());

            Assert.True((bool)result["success"]!, "pipeline failed: " + result.ToString(Formatting.Indented));
            Assert.Equal(3, (int)result["rowsIn"]!);
            Assert.Equal(3, (int)result["rowsOut"]!);

            // ── Dispatch 1: enriched CSV - original columns preserved, internal schema appended, enrichment column last ──
            var csvLines = File.ReadAllLines(Path.Combine(workDir, "out", "enriched-orders.csv"));
            Assert.Equal(
                "OrderID,CustomerCode,Region,Amount,CurrencyCode,OrderNumber,CustomerRef,RegionName,AmountUSD,Currency,AmountNZD",
                csvLines[0]);
            Assert.Equal(4, csvLines.Length);

            var row1 = csvLines[1].Split(','); // ORD-1001 / cust-a / North / 100 USD
            Assert.Equal("ORD-1001", row1[5]);  // OrderNumber mapped from OrderID
            Assert.Equal("CUST-A", row1[6]);    // CustomerRef uppercased from CustomerCode

            // Enrichment values must match the lookup API's convertedAmount for identical parameters.
            var expectedUsd = await ConvertedAmountAsync("USD", 100);
            var expectedEur = await ConvertedAmountAsync("EUR", 50);
            Assert.Equal(expectedUsd, double.Parse(row1[10], CultureInfo.InvariantCulture));
            var row2 = csvLines[2].Split(','); // ORD-1002 / cust-b / South / 50 EUR
            Assert.Equal(expectedEur, double.Parse(row2[10], CultureInfo.InvariantCulture));

            // ── Dispatch 2: filtered North-region subset as JSON ──
            var north = JArray.Parse(File.ReadAllText(Path.Combine(workDir, "out", "north-orders.json")));
            Assert.Equal(2, north.Count);
            Assert.All(north, r => Assert.Equal("North", (string)r["RegionName"]!));
            Assert.Contains(north, r => (string)r["OrderNumber"] == "ORD-1001");
            Assert.Contains(north, r => (string)r["OrderNumber"] == "ORD-1003");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task Transformation_ResolvesTargetByImportSchemaAttributeId()
    {
        // Proves schemaMap targets can be referenced by entity attribute ID instead of name: the executor
        // builds an id->name universe from DataSource.ImportSchema, and ApplySchemaMap falls back to
        // ResolveById when TargetAttribute is absent.
        const string profileJson = """
            {
              "DataExchangeProfileName": "ID Mapping Proof",
              "IsActive": true,
              "DataSource": {
                "DataSourceName": "inline-rows",
                "MediumType": "File",
                "ImportSchema": {
                  "AttributeDomainName": "Internal Order Schema",
                  "Version": "1",
                  "Attributes": [
                    { "EntityAttributeId": 10, "AttributeName": "OrderNumber", "DataType": "String" },
                    { "EntityAttributeId": 20, "AttributeName": "CustomerRef", "DataType": "String" }
                  ]
                }
              },
              "Pipeline": {
                "PipelineName": "IdMappingProof",
                "PipelineStages": [
                  {
                    "StageType": "DataTreatment",
                    "ExecutionOrder": 1,
                    "PipelineStageActions": [
                      {
                        "ExecutionOrder": 1,
                        "Action": {
                          "ActionName": "MapByIdsOnly",
                          "Type": "Transformation",
                          "SchemaMap": {
                            "AttributeMappings": [
                              { "TargetAttributeId": 10, "SourceAttributes": [{ "AttributeName": "OrderID" }], "TransformType": "DirectCopy", "MergeStrategy": "OverwriteExisting" },
                              { "TargetAttributeId": 20, "SourceAttributes": [{ "AttributeName": "CustomerCode" }], "TransformType": "ToUpper", "MergeStrategy": "OverwriteExisting" }
                            ]
                          }
                        }
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var profile = JsonConvert.DeserializeObject<DataExchangeProfile>(profileJson)!;
        var executor = _factory.Services.GetRequiredService<DataExchangeExecutor>();
        var input = new JObject
        {
            ["rows"] = new JArray(
                new JObject { ["OrderID"] = "ORD-9001", ["CustomerCode"] = "cust-x" },
                new JObject { ["OrderID"] = "ORD-9002", ["CustomerCode"] = "cust-y" })
        };

        var result = await executor.ExecuteProfileAsync(profile, input);

        Assert.True((bool)result["success"]!, "pipeline failed: " + result.ToString(Formatting.Indented));
        Assert.Equal("input.rows", (string)result["source"]!);
        Assert.Equal(2, (int)result["rowsOut"]!);

        var rows = (JArray)result["enrichedRows"]!;
        // Targets resolved by ID from ImportSchema: 10 -> OrderNumber, 20 -> CustomerRef.
        Assert.Equal("ORD-9001", (string)rows[0]["OrderNumber"]!);
        Assert.Equal("CUST-X", (string)rows[0]["CustomerRef"]!);
        Assert.Equal("ORD-9002", (string)rows[1]["OrderNumber"]!);
        Assert.Equal("CUST-Y", (string)rows[1]["CustomerRef"]!);
    }

    [Fact]
    public void DataTableToRows_MapsColumnsAndDbNull()
    {
        var table = new DataTable();
        table.Columns.Add("OrderNumber", typeof(string));
        table.Columns.Add("AmountUSD", typeof(decimal));
        table.Columns.Add("RegionName", typeof(string));

        table.Rows.Add("ORD-1", 9.5m, DBNull.Value);
        table.Rows.Add("ORD-2", DBNull.Value, "North");

        var rows = DataExchangeExecutor.DataTableToRows(table);

        Assert.Equal(2, rows.Count);
        Assert.Equal("ORD-1", (string)rows[0]["OrderNumber"]!);
        Assert.Equal(9.5m, (decimal)rows[0]["AmountUSD"]!);
        Assert.Equal(JTokenType.Null, rows[0]["RegionName"]!.Type);
        Assert.Equal(JTokenType.Null, rows[1]["AmountUSD"]!.Type);
        Assert.Equal("North", (string)rows[1]["RegionName"]!);
    }

    private async Task<double> ConvertedAmountAsync(string from, double amount)
    {
        var resp = await _fxClient.GetAsync($"/api/fake/exchange-rate?from={from}&to=NZD&amt={amount.ToString(CultureInfo.InvariantCulture)}");
        resp.EnsureSuccessStatusCode();
        return (double)JObject.Parse(await resp.Content.ReadAsStringAsync())["convertedAmount"]!;
    }
}
