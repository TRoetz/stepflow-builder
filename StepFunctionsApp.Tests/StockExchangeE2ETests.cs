using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities.DataSource;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests;

/// <summary>
/// End-to-end test of the "customer stock file to internal store page" Data Exchange: a customer CSV in
/// the external schema (SKU-ID, PRODUCT-CODE, PRODUCT-CATAGORY, ...) is mapped to the internal store-page
/// attributes (SKU-ID, PRODUCT_ID, CATEGORY, WEB-BREAD-CRUM, PRODUCTNAME, QTY, PRICE), enriched with a
/// category-to-web-breadcrumb lookup against the local fake host, and dispatched per row into the fake
/// store-page inventory API. Two paths are covered: direct profile execution via the executor, and
/// file-monitor driven processing of a CSV dropped into the profile inbox. Hermetic: fixtures embedded,
/// all directories isolated to temp, fake APIs on a local Kestrel socket - no external services or
/// machine-specific files required. The two tests use disjoint SKU ranges so shared in-memory store state
/// never couples their assertions.
/// </summary>
public sealed class StockExchangeE2ETests : IClassFixture<StockExchangeE2ETests.Factory>, IClassFixture<StockExchangeE2ETests.FakeFactory>
{
    /// <summary>Main app host with isolated flow-state + Data Exchange directories; the file monitor polls every second.</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _flowStateDir = Path.Combine(Path.GetTempPath(), "stepflow-tests", Guid.NewGuid().ToString("N"));
        private readonly string _dxRoot = Path.Combine(Path.GetTempPath(), "dataexchange-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [FlowStateOptions.SectionName + ":DiskPath"] = _flowStateDir,
                    [DataExchangeOptions.SectionName + ":ProfilesDirectory"] = Path.Combine(_dxRoot, "profiles"),
                    [DataExchangeOptions.SectionName + ":InboxDirectory"] = Path.Combine(_dxRoot, "inbox"),
                    [DataExchangeOptions.SectionName + ":OutputDirectory"] = Path.Combine(_dxRoot, "output"),
                    // Active monitor: the drop-file test relies on it picking up inbox files.
                    [DataExchangeOptions.SectionName + ":PollIntervalSeconds"] = "1",
                }));

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { Directory.Delete(_flowStateDir, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(_dxRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Fake test API host (Stepflow-Builder-Tests) on a real Kestrel socket - the breadcrumb lookup and store-page endpoints live here.</summary>
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

    /// <summary>Customer stock file in the external schema, as delivered by the customer.</summary>
    private const string StockCsvFixtureA =
        "SKU-ID,PRODUCT-CODE,PRODUCT-CATAGORY,PRODUCT-NAME,PRODUCT-DESCRIPTION,QTY,COST,RETAIL-PRICE\n" +
        "SKU-001,PC-100,Electronics,USB-C Hub 7-in-1,Aluminium multiport hub with HDMI and SD reader,25,8.40,39.95\n" +
        "SKU-002,PC-200,Kitchen & Dining,Cast Iron Skillet 26cm,Pre-seasoned enameled cast iron skillet,10,14.20,79.00\n" +
        "SKU-003,PC-300,Garden & Outdoor,Solar Path Lights 4-pack,Stainless steel solar LED path lights,40,5.10,44.99";

    /// <summary>Second customer delivery (disjoint SKU range) for the monitor-driven test.</summary>
    private const string StockCsvFixtureB =
        "SKU-ID,PRODUCT-CODE,PRODUCT-CATAGORY,PRODUCT-NAME,PRODUCT-DESCRIPTION,QTY,COST,RETAIL-PRICE\n" +
        "SKU-101,PC-400,Toys & Games,Retro Board Game,Classic strategy board game with wooden pieces,8,22.50,69.95\n" +
        "SKU-102,PC-500,Sports & Fitness,Yoga Mat 6mm,TPE non-slip yoga mat with carry strap,30,7.80,49.95\n" +
        "SKU-103,PC-600,Electronics,Bluetooth Speaker Mini,Waterproof portable speaker with 12h battery,15,12.30,59.95";

    /// <summary>Profile under test: __WORKDIR__ is rewritten to the isolated temp dir; the fake-host URL is rewritten to the local Kestrel socket.</summary>
    private const string ProfileTemplate = """
        {
          "DataExchangeProfileName": "Stock File To Store Page",
          "ProfileId": "stock-store-page",
          "IsActive": true,
          "DataSource": {
            "DataSourceName": "stock.csv",
            "MediumType": "File",
            "MediumConfigurationJson": "{\"filePath\": \"__WORKDIR__/stock.csv\"}"
          },
          "Pipeline": {
            "PipelineName": "StockToStorePage",
            "Description": "Map customer stock CSV to the internal store-page schema, enrich WEB-BREAD-CRUM via a category lookup API, and dispatch each row into the store-page inventory API.",
            "PipelineStages": [
              {
                "StageType": "DataTreatment",
                "ExecutionOrder": 1,
                "PipelineStageActions": [
                  {
                    "ExecutionOrder": 1,
                    "Action": {
                      "ActionName": "MapToStorePageSchema",
                      "Type": "Transformation",
                      "SchemaMap": {
                        "AttributeMappings": [
                          {
                            "TargetAttribute": { "AttributeName": "SKU-ID" },
                            "SourceAttributes": [{ "AttributeName": "SKU-ID" }],
                            "TransformType": "DirectCopy",
                            "MergeStrategy": "OverwriteExisting"
                          },
                          {
                            "TargetAttribute": { "AttributeName": "PRODUCT_ID" },
                            "SourceAttributes": [{ "AttributeName": "PRODUCT-CODE" }],
                            "TransformType": "DirectCopy",
                            "MergeStrategy": "OverwriteExisting"
                          },
                          {
                            "TargetAttribute": { "AttributeName": "CATEGORY" },
                            "SourceAttributes": [{ "AttributeName": "PRODUCT-CATAGORY" }],
                            "TransformType": "Trim",
                            "MergeStrategy": "OverwriteExisting"
                          },
                          {
                            "TargetAttribute": { "AttributeName": "PRODUCTNAME" },
                            "SourceAttributes": [{ "AttributeName": "PRODUCT-NAME" }],
                            "TransformType": "DirectCopy",
                            "MergeStrategy": "OverwriteExisting"
                          },
                          {
                            "TargetAttribute": { "AttributeName": "QTY" },
                            "SourceAttributes": [{ "AttributeName": "QTY" }],
                            "TransformType": "DirectCopy",
                            "MergeStrategy": "OverwriteExisting"
                          },
                          {
                            "TargetAttribute": { "AttributeName": "PRICE" },
                            "SourceAttributes": [{ "AttributeName": "RETAIL-PRICE" }],
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
                      "ActionName": "EnrichWebBreadCrum",
                      "Type": "EnrichmentLookup",
                      "OutputParameterName": "WEB-BREAD-CRUM",
                      "Lookup": {
                        "LookupName": "category-breadcrumb",
                        "LookupEndpoint": "http://localhost:5095/api/fake/categories/breadcrumb?category={CATEGORY}",
                        "Type": "Api",
                        "ValueFieldToReturn": "breadcrumb"
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
                      "ActionName": "AddStockItemsToStorePage",
                      "Type": "Dispatch",
                      "Endpoint": { "ActionEndpointURL": "http://localhost:5095/api/fake/store-page/items" }
                    }
                  }
                ]
              }
            ]
          }
        }
        """;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _fakeClient;
    private readonly DataExchangeOptions _dxOptions;

    public StockExchangeE2ETests(Factory factory, FakeFactory fakeFactory)
    {
        _factory = factory;
        if (string.IsNullOrEmpty(fakeFactory.BaseUrl)) fakeFactory.StartKestrel();
        _fakeClient = new HttpClient { BaseAddress = new Uri(fakeFactory.BaseUrl) };
        _dxOptions = factory.Services.GetRequiredService<IOptions<DataExchangeOptions>>().Value;
    }

    [Fact]
    public async Task DirectExecute_MapsEnrichesAndAddsStockItems()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "stock-exchange-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            File.WriteAllText(Path.Combine(workDir, "stock.csv"), StockCsvFixtureA);

            var profile = JsonConvert.DeserializeObject<DataExchangeProfile>(BuildProfileJson(workDir))!;
            var executor = _factory.Services.GetRequiredService<DataExchangeExecutor>();
            var result = await executor.ExecuteProfileAsync(profile, new JObject());

            Assert.True((bool)result["success"]!, "execution failed: " + result.ToString(Formatting.None));
            Assert.Equal(3, (int)result["rowsIn"]!);
            Assert.Equal(3, (int)result["rowsOut"]!);
            Assert.Equal(0, (int)result["rejectedCount"]!);

            var dispatch = (JObject)result["dispatched"]![0]!;
            Assert.Equal("POST", (string)dispatch["method"]!);
            Assert.Contains("/api/fake/store-page/items", (string)dispatch["endpoint"]!);
            Assert.Equal(3, (int)dispatch["rows"]!);
            Assert.Equal(3, (int)dispatch["ok"]!);
            Assert.Equal(0, (int)dispatch["failed"]!);

            // Enriched rows carry the mapped internal attributes plus the lookup value.
            var rows = result["enrichedRows"]!.ToObject<JArray>()!;
            var sku1 = rows.Single(r => (string)r["SKU-ID"] == "SKU-001") as JObject;
            Assert.Equal("PC-100", (string)sku1!["PRODUCT_ID"]);
            Assert.Equal("Electronics", (string)sku1["CATEGORY"]);
            Assert.Equal("USB-C Hub 7-in-1", (string)sku1["PRODUCTNAME"]);
            Assert.Equal(25, (int)sku1["QTY"]);
            Assert.Equal(39.95, (double)sku1["PRICE"], precision: 4);

            // Breadcrumb enrichment matches the fake lookup endpoint's own answer for that category.
            var expected = await BreadcrumbAsync("Electronics");
            Assert.Equal(expected, (string)sku1["WEB-BREAD-CRUM"]);

            // The store page received all three items with mapped + enriched fields.
            foreach (var (sku, product, category, name, qty, price) in new[]
                     {
                         ("SKU-001", "PC-100", "Electronics", "USB-C Hub 7-in-1", 25, 39.95),
                         ("SKU-002", "PC-200", "Kitchen & Dining", "Cast Iron Skillet 26cm", 10, 79.00),
                         ("SKU-003", "PC-300", "Garden & Outdoor", "Solar Path Lights 4-pack", 40, 44.99)
                     })
            {
                var item = await GetStoreItemAsync(sku);
                Assert.Equal(1, (int)item["count"]!);
                var stored = (JObject)item["items"]![0]!;
                Assert.Equal(product, (string)stored!["productId"]);
                Assert.Equal(category, (string)stored["category"]);
                Assert.Equal(await BreadcrumbAsync(category), (string)stored["webBreadCrum"]);
                Assert.Equal(name, (string)stored["productName"]);
                Assert.Equal(qty, (int)stored["qty"]);
                Assert.Equal(price, (double)stored["price"], precision: 4);
            }
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task MonitorDrop_ProcessesFileAndAddsStockItems()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "stock-exchange-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            File.WriteAllText(Path.Combine(workDir, "stock.csv"), StockCsvFixtureB);

            // Persist the profile so the file monitor's LoadAll() discovers it.
            var store = _factory.Services.GetRequiredService<DataExchangeProfileStore>();
            var id = store.Save(JsonConvert.DeserializeObject<DataExchangeProfile>(BuildProfileJson(workDir))!);
            Assert.Equal("stock-store-page", id);

            // Drop the customer file into this profile's inbox.
            var inbox = Path.Combine(_dxOptions.InboxDirectory, id);
            Directory.CreateDirectory(inbox);
            File.WriteAllText(Path.Combine(inbox, "stock.csv"), StockCsvFixtureB);

            // The monitor polls every second and skips files modified within the last 2 seconds, so allow a few cycles.
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && !await AllStoreItemsPresentAsync(new[] { "SKU-101", "SKU-102", "SKU-103" }))
                await Task.Delay(500);

            Assert.True(await AllStoreItemsPresentAsync(new[] { "SKU-101", "SKU-102", "SKU-103" }),
                "monitor did not process the dropped file within 30s");

            var item = await GetStoreItemAsync("SKU-102");
            Assert.Equal(1, (int)item["count"]!);
            var stored = (JObject)item["items"]![0]!;
            Assert.Equal("PC-500", (string)stored!["productId"]);
            Assert.Equal("Sports & Fitness", (string)stored["category"]);
            Assert.Equal(await BreadcrumbAsync("Sports & Fitness"), (string)stored["webBreadCrum"]);
            Assert.Equal(30, (int)stored["qty"]);

            // The source file was archived to processed/ and a successful execution artifact was recorded.
            var deadline2 = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline2 && Directory.GetFiles(inbox).Length > 0) await Task.Delay(250);
            Assert.Empty(Directory.GetFiles(inbox));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(inbox, "processed"), "*stock.csv"));

            var executions = await ExecutionsAsync();
            Assert.Contains(executions, e => (string?)e["profileId"] == id && (bool?)e["success"] == true);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    private string BuildProfileJson(string workDir) => ProfileTemplate
        .Replace("__WORKDIR__", workDir.Replace('\\', '/'))
        .Replace("http://localhost:5095", _fakeClient.BaseAddress!.ToString().TrimEnd('/'));

    private async Task<string> BreadcrumbAsync(string category)
    {
        var resp = await _fakeClient.GetAsync($"/api/fake/categories/breadcrumb?category={Uri.EscapeDataString(category)}");
        resp.EnsureSuccessStatusCode();
        return (string)JObject.Parse(await resp.Content.ReadAsStringAsync())["breadcrumb"]!;
    }

    private async Task<JObject> GetStoreItemAsync(string skuId)
    {
        var resp = await _fakeClient.GetAsync($"/api/fake/store-page/items?skuId={Uri.EscapeDataString(skuId)}");
        resp.EnsureSuccessStatusCode();
        return JObject.Parse(await resp.Content.ReadAsStringAsync());
    }

    private async Task<bool> AllStoreItemsPresentAsync(string[] skus)
    {
        foreach (var sku in skus)
        {
            var item = await GetStoreItemAsync(sku);
            if ((int)item["count"]! != 1) return false;
        }
        return true;
    }

    private async Task<JArray> ExecutionsAsync()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/data-exchange/executions?limit=50");
        resp.EnsureSuccessStatusCode();
        return JArray.Parse(await resp.Content.ReadAsStringAsync());
    }
}
