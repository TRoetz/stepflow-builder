using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using StepFunctionsApp;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests;

/// <summary>
/// End-to-end flow tests: loads the FakeData_*.json flows from Flows/, rewrites their
/// http://localhost:5095 fake-API base URL to the local fake test host, registers them through
/// POST /api/state-machines and executes them via execute-sync. Proves the fake data APIs
/// work as flow resources end-to-end (HTTP invoker, Choice rules, DuckDB transform).
/// Two hosts run per class: the main app on an in-memory TestServer (registration/execution) and
/// the Stepflow-Builder-Tests fake API host on a real Kestrel socket (the flows' HTTP resources).
/// </summary>
public sealed class FakeDataFlowTests : IClassFixture<FakeDataFlowTests.Factory>, IClassFixture<FakeDataFlowTests.FakeFactory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        // Isolate this host's durable flow-state store in a fresh temp dir so startup recovery never loads
        // checkpoints from other runs (their embedded definitions carry stale ephemeral ports).
        private readonly string _flowStateDir = Path.Combine(Path.GetTempPath(), "stepflow-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { [FlowStateOptions.SectionName + ":DiskPath"] = _flowStateDir }));

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { Directory.Delete(_flowStateDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Fake test API host (Stepflow-Builder-Tests) on a real Kestrel socket - the flows' HTTP resources live here.</summary>
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

    private readonly HttpClient _client;
    private readonly string _fakeBaseUrl;
    private readonly string _flowsDir;

    public FakeDataFlowTests(Factory factory, FakeFactory fakeFactory)
    {
        if (string.IsNullOrEmpty(fakeFactory.BaseUrl)) fakeFactory.StartKestrel();
        _client = factory.CreateClient();
        _fakeBaseUrl = fakeFactory.BaseUrl;
        _flowsDir = Path.Combine(factory.Services.GetRequiredService<IHostEnvironment>().ContentRootPath, "Flows");
    }

    /// <summary>Registers a flow file (URL-rewritten to the test server) and executes it synchronously.</summary>
    private async Task<JObject> RunFlowAsync(string flowFile, JObject? input = null)
    {
        var raw = await File.ReadAllTextAsync(Path.Combine(_flowsDir, flowFile));

        // Point the flow's fake-data URLs at the fake test API host.
        raw = raw.Replace("http://localhost:5095", _fakeBaseUrl);
        var def = ParseJson(raw);

        var regPayload = new JObject
        {
            ["name"] = $"e2e-{flowFile}",
            ["description"] = "E2E test registration of a fake-data flow",
            ["startAt"] = (string)def["StartAt"],
            ["states"] = def["States"]!
        };
        var regResp = await _client.PostAsync("/api/state-machines", JsonContent(regPayload));
        Assert.True(
            regResp.IsSuccessStatusCode,
            $"Registration of {flowFile} failed: {(int)regResp.StatusCode} {await regResp.Content.ReadAsStringAsync()}");

        var id = (string)ParseJson(await regResp.Content.ReadAsStringAsync())["id"];

        var execResp = await _client.PostAsync($"/api/flows/execute-sync/{Uri.EscapeDataString(id!)}", JsonContent(input ?? new JObject()));
        Assert.True(
            execResp.IsSuccessStatusCode,
            $"Execution of {flowFile} failed: {(int)execResp.StatusCode} {await execResp.Content.ReadAsStringAsync()}");

        var result = ParseJson(await execResp.Content.ReadAsStringAsync());
        if ((string)result["status"] != "Succeeded")
            Assert.Fail($"Flow {flowFile} ended in {(string)result["status"]}: error={(string)result["errorCode"]} message={(string)result["errorMessage"]} output={result["output"]}");
        return result;
    }

    private static StringContent JsonContent(JToken token) =>
        new(token.ToString(Formatting.None), Encoding.UTF8, "application/json");

    // Parse with date strings left as strings - default JObject.Parse converts ISO dates to JValue(DateTime), which breaks string assertions via culture-dependent ToString().
    private static JObject ParseJson(string text)
    {
        using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    // ── FakeData_WeatherCheck.json ─────────────────────────────────────────────

    [Fact]
    public async Task WeatherCheckFlow_SucceedsAndReportPassesValidation()
    {
        var result = await RunFlowAsync("FakeData_WeatherCheck.json");

        Assert.Equal("Succeeded", (string)result["status"]);
        var outp = result["output"]!;

        Assert.Equal("Wellington", (string)outp["city"]);
        Assert.Equal("celsius", (string)outp["units"]);
        Assert.InRange((double)outp["temperature"], -5, 38);
        Assert.Equal(5, ((JArray)outp["forecast"]).Count);
        Assert.Equal("2026-08-19T06:30:00Z", (string)outp["observedAt"]);
    }

    // ── FakeData_GoldAndFx.json ────────────────────────────────────────────────

    [Fact]
    public async Task GoldAndFxFlow_ChainsGoldTotalIntoConversion()
    {
        var result = await RunFlowAsync("FakeData_GoldAndFx.json");

        Assert.Equal("Succeeded", (string)result["status"]);
        var outp = result["output"]!;

        // The conversion amount must be the 2-ounce NZD gold total from step 1.
        Assert.Equal(9043.5, (double)outp["amount"]);
        Assert.Equal("NZD", (string)outp["from"]);
        Assert.Equal("USD", (string)outp["to"]);

        // Same arithmetic the controller performs: cross rate from the per-USD table.
        var rate = 1.0 / 1.6327;
        Assert.Equal(Math.Round(rate, 6), (double)outp["baseRate"]);
        Assert.Equal(Math.Round(9043.5 * rate, 4), (double)outp["convertedAmount"]);
    }

    // ── FakeData_LargeJsonAggregate.json ───────────────────────────────────────

    [Fact]
    public async Task LargeJsonAggregateFlow_AggregatesAllRecords()
    {
        var result = await RunFlowAsync("FakeData_LargeJsonAggregate.json");

        Assert.Equal("Succeeded", (string)result["status"]);
        var outp = result["output"]!;

        // All four regions present, every one of the 500 records counted exactly once.
        Assert.Equal(4, (int)outp["rowCount"]);
        var rows = (JArray)outp["rows"];
        Assert.Equal(500, rows.Sum(r => (long)r["employees"]));

        foreach (var row in rows)
            Assert.InRange((double)row["avg_salary"], 35_000, 130_000);
    }

    // ── FakeData_XmlInvoices.json ──────────────────────────────────────────────

    [Fact]
    public async Task XmlInvoicesFlow_WrapsNonJsonBodyAndValidatesStructure()
    {
        var result = await RunFlowAsync("FakeData_XmlInvoices.json");

        Assert.Equal("Succeeded", (string)result["status"]);
        var body = (string)result["output"]!["body"];
        Assert.StartsWith("<Invoices", body);

        // The wrapped XML must survive the engine intact and be well-formed.
        var doc = XDocument.Parse(body);
        var root = doc.Root!;
        Assert.Equal("Invoices", root.Name.LocalName);
        Assert.Equal("5", (string)root.Attribute("count"));

        var invoices = root.Elements().ToList();
        Assert.Equal(5, invoices.Count);
        foreach (var inv in invoices)
        {
            var items = inv.Elements("LineItems").Single().Elements("Item").ToList();
            var sum = items.Sum(it => double.Parse(it.Element("LineTotal")!.Value, CultureInfo.InvariantCulture));
            var total = double.Parse(inv.Element("Total")!.Value, CultureInfo.InvariantCulture);
            Assert.True(Math.Abs(total - sum) < 0.01, $"Invoice {(string)inv.Attribute("id")} totals mismatch");
        }
    }

    // ── FakeData_CsvImportPipeline.json ────────────────────────────────────────

    [Fact]
    public async Task CsvImportPipelineFlow_ImportsAndAggregatesCsv()
    {
        var result = await RunFlowAsync("FakeData_CsvImportPipeline.json");

        Assert.Equal("Succeeded", (string)result["status"]);
        var outp = result["output"]!;

        // All 20 downloaded rows must be imported and aggregated.
        Assert.True((int)outp["rowCount"] >= 1);
        var rows = (JArray)outp["rows"];
        Assert.Equal(20, rows.Sum(r => (long)r["orders"]));
        foreach (var row in rows)
            Assert.True((double)row["total_amount"] > 0);
    }
}
