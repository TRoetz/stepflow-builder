using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.StepFunctions;
using StepFunctionsApp.Workspace;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Named rule catalog endpoint tests — CRUD round trip against /api/rules (the project's rules.json).
    /// Rule names may contain '/' (flow-derived artifacts are named "&lt;Flow&gt;/&lt;State&gt;"), so the route is
    /// {*name} and clients must URL-encode. Save validates name/kind/definition, and engine-backed kinds
    /// (sql, ms-rules) validate their definition body — all failures surface as 400 { error }.
    /// </summary>
    public sealed class RulesEndpointTests : IClassFixture<RulesEndpointTests.Factory>
    {
        private readonly HttpClient _client;

        // Unique name per test run (the factory isolates rules.json, but unique names keep failures readable).
        private static readonly string _name = "e2e/" + Guid.NewGuid().ToString("N");

        public RulesEndpointTests(Factory factory)
        {
            _client = factory.CreateClient();
        }

        private static async Task<(HttpStatusCode Status, JToken Body)> SendAsync(HttpClient client, HttpMethod method, string url, object? payload)
        {
            var content = payload == null ? null : new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var res = await client.SendAsync(new HttpRequestMessage(method, url) { Content = content });
            var text = await res.Content.ReadAsStringAsync();
            return (res.StatusCode, string.IsNullOrWhiteSpace(text) ? JValue.CreateNull() : JToken.Parse(text));
        }

        /// <summary>
        /// Mirrors the UI's rulePath(): encode each '/'-segment separately and keep the slashes raw.
        /// The {*name} catch-all captures raw slashes; a %2F-encoded slash is NOT decoded by routing, so
        /// whole-name encoding would 404 on flow-derived names like "&lt;Flow&gt;/&lt;State&gt;".
        /// </summary>
        private static string RulePath(string name) => string.Join("/", name.Split('/').Select(Uri.EscapeDataString));

        [Fact]
        public async Task Crud_FullRoundTrip_IncludingSlashNames()
        {
            var def = new JObject
            {
                ["conditions"] = new JArray(new JObject { ["expression"] = "$.x > 1", ["next"] = "StateA" }),
                ["defaultNext"] = "StateB"
            };

            // Save (create) — the name contains '/', so GET/DELETE URLs must be encoded.
            var (s1, b1) = await SendAsync(_client, HttpMethod.Post, "/api/rules", new { name = _name, kind = "choice", description = "e2e rule", definition = def });
            Assert.Equal(HttpStatusCode.OK, s1);
            Assert.Equal("choice", (string)b1!["kind"]!);
            Assert.NotNull(b1["updatedAtUtc"]);

            // List shows the catalog entry.
            var (s2, b2) = await SendAsync(_client, HttpMethod.Get, "/api/rules", null);
            Assert.Equal(HttpStatusCode.OK, s2);
            var entry = ((JArray)b2!).FirstOrDefault(r => string.Equals((string)r!["name"], _name, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(entry);
            Assert.Equal("choice", (string)entry!["kind"]!);

            // Get by encoded name returns the full definition.
            var (s3, b3) = await SendAsync(_client, HttpMethod.Get, "/api/rules/" + RulePath(_name), null);
            Assert.Equal(HttpStatusCode.OK, s3);
            Assert.Equal("e2e rule", (string)b3!["description"]!);
            Assert.Equal("$.x > 1", (string)b3["definition"]["conditions"][0]["expression"]!);

            // Upsert replaces in place.
            var (s4, _) = await SendAsync(_client, HttpMethod.Post, "/api/rules", new { name = _name, kind = "choice", description = "updated", definition = def });
            Assert.Equal(HttpStatusCode.OK, s4);
            var (s5, b5) = await SendAsync(_client, HttpMethod.Get, "/api/rules/" + RulePath(_name), null);
            Assert.Equal("updated", (string)b5!["description"]!);

            // Delete removes it; a second delete 404s.
            var (s6, b6) = await SendAsync(_client, HttpMethod.Delete, "/api/rules/" + RulePath(_name), null);
            Assert.Equal(HttpStatusCode.OK, s6);
            Assert.Equal(_name, (string)b6!["deleted"]!);

            var (s7, _) = await SendAsync(_client, HttpMethod.Get, "/api/rules/" + RulePath(_name), null);
            Assert.Equal(HttpStatusCode.NotFound, s7);
            var (s8, b8) = await SendAsync(_client, HttpMethod.Delete, "/api/rules/" + RulePath(_name), null);
            Assert.Equal(HttpStatusCode.NotFound, s8);
            Assert.Contains(_name, (string)b8!["error"]!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Get_Unknown_Returns404_NamingTheRule()
        {
            var missing = "no-such-rule-" + Guid.NewGuid().ToString("N");
            var (status, body) = await SendAsync(_client, HttpMethod.Get, "/api/rules/" + RulePath(missing), null);
            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.Contains(missing, (string)body!["error"]!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Save_InvalidNameKindOrDefinition_Returns400()
        {
            // Missing name.
            var (s1, b1) = await SendAsync(_client, HttpMethod.Post, "/api/rules", new { kind = "choice", definition = new JObject() });
            Assert.Equal(HttpStatusCode.BadRequest, s1);
            Assert.Contains("name", (string)b1!["error"]!, StringComparison.OrdinalIgnoreCase);

            // Unknown kind.
            var (s2, b2) = await SendAsync(_client, HttpMethod.Post, "/api/rules", new { name = "bad-kind-" + Guid.NewGuid().ToString("N"), kind = "bogus", definition = new JObject() });
            Assert.Equal(HttpStatusCode.BadRequest, s2);
            Assert.Contains("Unknown rule kind", (string)b2!["error"]!, StringComparison.OrdinalIgnoreCase);

            // sql without a boolean expression.
            var (s3, b3) = await SendAsync(_client, HttpMethod.Post, "/api/rules", new { name = "bad-sql-" + Guid.NewGuid().ToString("N"), kind = "sql", definition = new JObject() });
            Assert.Equal(HttpStatusCode.BadRequest, s3);
            Assert.Contains("expression", (string)b3!["error"]!, StringComparison.OrdinalIgnoreCase);

            // ms-rules without the rules array.
            var (s4, b4) = await SendAsync(_client, HttpMethod.Post, "/api/rules", new { name = "bad-ms-" + Guid.NewGuid().ToString("N"), kind = "ms-rules", definition = new JObject() });
            Assert.Equal(HttpStatusCode.BadRequest, s4);
            Assert.Contains("rules", (string)b4!["error"]!, StringComparison.OrdinalIgnoreCase);
        }

        // ── test host factory (isolated state dirs; rules.json isolated via Rules:Path) ──

        public sealed class Factory : WebApplicationFactory<Program>
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "stepflow-rules-tests", Guid.NewGuid().ToString("N"));

            protected override void ConfigureWebHost(IWebHostBuilder builder) =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [FlowStateOptions.SectionName + ":DiskPath"] = Path.Combine(_root, "flow-state"),
                        [FormDataOptions.SectionName + ":DatabasePath"] = Path.Combine(_root, "stepflow_data.db"),
                        [WorkspaceOptions.SectionName + ":RootDirectory"] = Path.Combine(_root, "workspace-data"),
                        [DataExchangeOptions.SectionName + ":ProfilesDirectory"] = Path.Combine(_root, "dx", "profiles"),
                        [DataExchangeOptions.SectionName + ":InboxDirectory"] = Path.Combine(_root, "dx", "inbox"),
                        [DataExchangeOptions.SectionName + ":OutputDirectory"] = Path.Combine(_root, "dx", "output"),
                        ["Rules:Path"] = Path.Combine(_root, "rules.json"),
                    }));

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
