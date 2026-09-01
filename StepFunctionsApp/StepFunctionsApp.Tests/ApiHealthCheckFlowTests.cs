using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests;

/// <summary>
/// End-to-end test of the "API Health Check" template flow: an HTTP GET with includeStatus produces a
/// {status, ok, body} envelope instead of failing on non-2xx, and a Choice state carrying a JSONata
/// Expression rule ("$.status &lt; 400") branches to a healthy or unhealthy report. The definitions mirror the
/// exact ASL the UI exports for tpl-api-health-check (http Task with includeStatus parameter, Choice with an
/// Expression rule, jsonata transform reports). Hermetic: fake API host on an ephemeral loopback Kestrel
/// socket; the "unhealthy" endpoint is a deliberately unknown route (a real 404 from Kestrel).
/// </summary>
public sealed class ApiHealthCheckFlowTests : IClassFixture<ApiHealthCheckFlowTests.Factory>, IClassFixture<ApiHealthCheckFlowTests.FakeFactory>
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

    /// <summary>Fake test API host (Stepflow-Builder-Tests) on a real Kestrel socket - the health-check HTTP resources live here.</summary>
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

    public ApiHealthCheckFlowTests(Factory factory, FakeFactory fakeFactory)
    {
        if (string.IsNullOrEmpty(fakeFactory.BaseUrl)) fakeFactory.StartKestrel();
        _client = factory.CreateClient();
        _fakeBaseUrl = fakeFactory.BaseUrl;
    }

    /// <summary>The exact ASL the UI exports for tpl-api-health-check, with the endpoint URL rewritten to the fake host.</summary>
    private JObject BuildHealthCheckFlow(string healthUrl, bool includeStatus) => new()
    {
        ["name"] = "api-health-check-e2e",
        ["description"] = "E2E test of the API Health Check template (includeStatus envelope + Expression choice)",
        ["startAt"] = "Start",
        ["states"] = new JObject
        {
            // stepflow:terminal:start -> Pass
            ["Start"] = new JObject { ["Type"] = "Pass", ["Next"] = "CheckEndpoint" },
            // stepflow:api:http with includeStatus (query params ride in the URL, as the UI exports them)
            ["CheckEndpoint"] = new JObject
            {
                ["Type"] = "Task",
                ["Resource"] = healthUrl,
                ["Parameters"] = BuildHttpParameters(includeStatus),
                ["Next"] = "Healthy?"
            },
            // stepflow:flow:choice -> Choice with a JSONata Expression rule
            ["Healthy?"] = new JObject
            {
                ["Type"] = "Choice",
                ["Choices"] = new JArray
                {
                    new JObject { ["Expression"] = "$.status < 400", ["Next"] = "ReportOk" }
                },
                ["Default"] = "ReportIssue"
            },
            // stepflow:transform:jsonata reports
            ["ReportOk"] = JsonataState("{ \"healthy\": true, \"status\": $.status }"),
            ["ReportIssue"] = JsonataState("{ \"healthy\": false, \"status\": $.status }"),
            // stepflow:terminal:end -> Pass + end:true (no Next; terminates the run)
            ["End"] = new JObject { ["Type"] = "Pass", ["end"] = true }
        }
    };

    private static JObject BuildHttpParameters(bool includeStatus)
    {
        var parameters = new JObject
        {
            ["__handler"] = "http",
            ["method"] = "GET"
        };
        if (includeStatus) parameters["includeStatus"] = true;
        return parameters;
    }

    private static JObject JsonataState(string expression) => new()
    {
        ["Type"] = "Task",
        ["Resource"] = "transform://jsonata",
        ["Parameters"] = new JObject { ["expression"] = expression, ["input_data.$"] = "$" },
        ["Next"] = "End"
    };

    /// <summary>Registers the definition and executes it synchronously; returns the execution result body.</summary>
    private async Task<JObject> RunFlowAsync(JObject definition)
    {
        var regResp = await _client.PostAsync("/api/state-machines", JsonContent(definition));
        Assert.True(
            regResp.IsSuccessStatusCode,
            $"Registration failed: {(int)regResp.StatusCode} {await regResp.Content.ReadAsStringAsync()}");
        var id = (string)ParseJson(await regResp.Content.ReadAsStringAsync())["id"];

        var execResp = await _client.PostAsync($"/api/flows/execute-sync/{Uri.EscapeDataString(id!)}", JsonContent(new JObject()));
        Assert.True(
            execResp.IsSuccessStatusCode,
            $"Execution request failed: {(int)execResp.StatusCode} {await execResp.Content.ReadAsStringAsync()}");
        return ParseJson(await execResp.Content.ReadAsStringAsync());
    }

    private static StringContent JsonContent(JToken token) =>
        new(token.ToString(Formatting.None), Encoding.UTF8, "application/json");

    // Parse with date strings left as strings - default JObject.Parse converts ISO dates to JValue(DateTime), which breaks string assertions via culture-dependent ToString().
    private static JObject ParseJson(string text)
    {
        using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    [Fact]
    public async Task HealthCheck_HealthyEndpoint_TakesTrueBranch_AndReportsStatus200()
    {
        // GET /api/fake/weather?city=Wellington returns 200 on the fake host.
        var result = await RunFlowAsync(BuildHealthCheckFlow($"{_fakeBaseUrl}/api/fake/weather?city=Wellington", includeStatus: true));

        Assert.True((string?)result["status"] == "Succeeded", $"expected Succeeded, got: {result}");
        var output = result["output"]!;
        Assert.True((bool)output["healthy"], $"expected healthy=true, got {output}");
        Assert.Equal(200, (int)output["status"]);
    }

    [Fact]
    public async Task HealthCheck_UnhealthyEndpoint_TakesFalseBranch_AndReportsStatus404()
    {
        // A deliberately unknown route: Kestrel answers 404. With includeStatus the non-2xx becomes data,
        // so the flow itself succeeds and the Expression choice routes to the failure report.
        var result = await RunFlowAsync(BuildHealthCheckFlow($"{_fakeBaseUrl}/api/fake/no-such-endpoint", includeStatus: true));

        Assert.True((string?)result["status"] == "Succeeded", $"expected Succeeded, got: {result}");
        var output = result["output"]!;
        Assert.False((bool)output["healthy"], $"expected healthy=false, got {output}");
        Assert.Equal(404, (int)output["status"]);
    }

    [Fact]
    public async Task HealthCheck_WithoutIncludeStatus_Non2xx_FailsExecution()
    {
        // Default behavior is unchanged: without includeStatus a non-2xx response fails the run.
        var result = await RunFlowAsync(BuildHealthCheckFlow($"{_fakeBaseUrl}/api/fake/no-such-endpoint", includeStatus: false));

        Assert.NotEqual("Succeeded", (string)result["status"]);
    }
}
