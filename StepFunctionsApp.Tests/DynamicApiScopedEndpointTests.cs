using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
    /// Dynamic API scoped-endpoint tests — proof that Kestrel really binds multiple ports and that port scoping
    /// flows through HttpContext.Connection.LocalPort: the management port serves every node, a node port serves
    /// only its own subtree (dynamic APIs + health) and 404s everything else. Real Kestrel (not TestServer) on
    /// ephemeral loopback ports; clients address each port explicitly so they never depend on which endpoint the
    /// factory picks as primary.
    /// </summary>
    public sealed class DynamicApiScopedEndpointTests : IClassFixture<DynamicApiScopedEndpointTests.Factory>
    {
        private readonly Factory _factory;
        private readonly HttpClient _mgmt;
        private readonly HttpClient _scoped;
        private readonly WorkspaceStore _workspace;
        private static readonly string _domain = "ScopedE2EDomain-" + Guid.NewGuid().ToString("N");

        public DynamicApiScopedEndpointTests(Factory factory)
        {
            _factory = factory;
            factory.Start(); // real Kestrel: binds ManagementPort + ScopedPort (idempotent)
            _mgmt = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{factory.ManagementPort}") };
            _scoped = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{factory.ScopedPort}") };
            _workspace = factory.Services.GetRequiredService<WorkspaceStore>();
        }

        // xunit instantiates this class per fact; every operation below is idempotent (create-or-replace), so re-runs converge.
        private async Task SetupAsync()
        {
            var scopedOrg = _workspace.CreateNode("ScopedOrg");
            var otherOrg = _workspace.CreateNode("OtherOrg");

            var (dStatus, _) = await SendAsync(_mgmt, HttpMethod.Post, "/api/attribute-domains", new { attributeDomain = new { attributeDomainName = _domain } });
            Assert.Equal(HttpStatusCode.OK, dStatus);

            await EnsureApiAsync(scopedOrg, "Scoped Rows", "/scoped-rows");
            await EnsureApiAsync(otherOrg, "Other Rows", "/other-rows");
        }

        private async Task EnsureApiAsync(string nodePath, string name, string basePath)
        {
            var (listStatus, listBody) = await SendAsync(_mgmt, HttpMethod.Get, $"/api/dynamic/apis?nodePath={Uri.EscapeDataString(nodePath)}", null);
            Assert.Equal(HttpStatusCode.OK, listStatus);
            if (((JArray)listBody!).Any(r => string.Equals((string)r!["basePath"], basePath, StringComparison.Ordinal))) return; // created by an earlier fact (shared fixture)

            var payload = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["nodePath"] = nodePath,
                ["basePath"] = basePath,
                ["attributeDomain"] = _domain,
                ["operations"] = new[]
                {
                    new { method = "GET", path = "", handlerType = "eav" },
                    new { method = "POST", path = "", handlerType = "eav" },
                },
            };
            var (status, _) = await SendAsync(_mgmt, HttpMethod.Post, "/api/dynamic/apis", payload);
            Assert.Equal(HttpStatusCode.Created, status);
        }

        private static async Task<(HttpStatusCode Status, JToken Body)> SendAsync(HttpClient client, HttpMethod method, string url, object? payload)
        {
            var content = payload == null ? null : new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var res = await client.SendAsync(new HttpRequestMessage(method, url) { Content = content });
            var text = await res.Content.ReadAsStringAsync();
            return (res.StatusCode, string.IsNullOrWhiteSpace(text) ? JValue.CreateNull() : JToken.Parse(text));
        }

        // ── management port: full surface + bindings listing ───────────────

        [Fact]
        public async Task ManagementPort_ServesAllNodes_AndListsBindings()
        {
            await SetupAsync();

            var (s1, b1) = await SendAsync(_mgmt, HttpMethod.Get, "/api/dynamic/scoped-rows", null);
            Assert.Equal(HttpStatusCode.OK, s1);
            Assert.NotNull(b1!["count"]);

            var (s2, _) = await SendAsync(_mgmt, HttpMethod.Get, "/api/dynamic/other-rows", null);
            Assert.Equal(HttpStatusCode.OK, s2);

            var (s3, b3) = await SendAsync(_mgmt, HttpMethod.Get, "/api/dynamic/endpoints", null);
            Assert.Equal(HttpStatusCode.OK, s3);
            Assert.Equal(_factory.ManagementPort, (int)b3!["managementPort"]!);
            var eps = (JArray)b3["endpoints"]!;
            Assert.Single(eps);
            Assert.Equal(_factory.ScopedPort, (int)eps[0]!["port"]!);
            Assert.Equal("ScopedOrg", (string)eps[0]["nodePath"]);
        }

        // ── node port: only its own subtree ────────────────────────────────

        [Fact]
        public async Task ScopedPort_ServesOnlyItsNodeSubtree()
        {
            await SetupAsync();
            var entityId = "scoped-e2e-" + Guid.NewGuid().ToString("N");

            // Full data path through the scoped port: POST a row, then read it back.
            var (s1, b1) = await SendAsync(_scoped, HttpMethod.Post, "/api/dynamic/scoped-rows", new { entityId, entityType = "Widget", name = "alpha" });
            Assert.Equal(HttpStatusCode.Created, s1);

            var (s2, b2) = await SendAsync(_scoped, HttpMethod.Get, $"/api/dynamic/scoped-rows?entityId={entityId}", null);
            Assert.Equal(HttpStatusCode.OK, s2);
            Assert.Equal(1, (int)b2!["count"]!);

            // Other nodes' APIs are excluded by scoping — dispatcher 404, not a handler failure.
            var (s3, b3) = await SendAsync(_scoped, HttpMethod.Get, "/api/dynamic/other-rows", null);
            Assert.Equal(HttpStatusCode.NotFound, s3);
            Assert.Contains("No dynamic API matches", (string)b3!["error"]!, StringComparison.OrdinalIgnoreCase);

            var (s4, b4) = await SendAsync(_scoped, HttpMethod.Post, "/api/dynamic/other-rows", new { entityId, entityType = "Widget" });
            Assert.Equal(HttpStatusCode.NotFound, s4);
            Assert.Contains("No dynamic API matches", (string)b4!["error"]!, StringComparison.OrdinalIgnoreCase);
        }

        // ── node port: management surface blocked ──────────────────────────

        [Fact]
        public async Task ScopedPort_BlocksManagementSurface()
        {
            await SetupAsync();

            // Definition CRUD is management-only — even for a payload that would be valid on the management port.
            var (s1, _) = await SendAsync(_scoped, HttpMethod.Get, "/api/dynamic/apis", null);
            Assert.Equal(HttpStatusCode.NotFound, s1);

            var createPayload = new Dictionary<string, object?>
            {
                ["name"] = "Scoped Blocked API",
                ["nodePath"] = "ScopedOrg",
                ["basePath"] = "/blocked-test",
                ["attributeDomain"] = _domain,
                ["operations"] = new[] { new { method = "GET", path = "", handlerType = "eav" } },
            };
            var (s2, _) = await SendAsync(_scoped, HttpMethod.Post, "/api/dynamic/apis", createPayload);
            Assert.Equal(HttpStatusCode.NotFound, s2); // guard 404 — not a 201

            // Builder UI root and MCP are management-only too.
            var (s3, _) = await SendAsync(_scoped, HttpMethod.Get, "/", null);
            Assert.Equal(HttpStatusCode.NotFound, s3);

            var (s4, _) = await SendAsync(_scoped, HttpMethod.Get, "/mcp", null);
            Assert.Equal(HttpStatusCode.NotFound, s4);

            // Health stays open on node ports.
            var (s5, b5) = await SendAsync(_scoped, HttpMethod.Get, "/api/health", null);
            Assert.Equal(HttpStatusCode.OK, s5);
            Assert.Equal("healthy", (string)b5!["status"]!);
        }

        // ── node port: OpenAPI spec scoped to the subtree ──────────────────

        [Fact]
        public async Task ScopedOpenApi_OnlyListsNodeApis()
        {
            await SetupAsync();

            var (_, scopedDoc) = await SendAsync(_scoped, HttpMethod.Get, "/api/dynamic/openapi.json", null);
            var scopedPaths = ((JObject)((JObject)scopedDoc!)["paths"]!).Properties().Select(p => p.Name).ToList();
            Assert.Contains("/scoped-rows", scopedPaths);
            Assert.DoesNotContain("/other-rows", scopedPaths);

            var (_, mgmtDoc) = await SendAsync(_mgmt, HttpMethod.Get, "/api/dynamic/openapi.json", null);
            var mgmtPaths = ((JObject)((JObject)mgmtDoc!)["paths"]!).Properties().Select(p => p.Name).ToList();
            Assert.Contains("/scoped-rows", mgmtPaths);
            Assert.Contains("/other-rows", mgmtPaths);
        }

        // ── test host factory (real Kestrel, isolated state dirs) ───────────

        public sealed class Factory : WebApplicationFactory<Program>
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "stepflow-scoped-endpoint-tests", Guid.NewGuid().ToString("N"));
            private bool _started;

            public int ManagementPort { get; } = GetFreePort();
            public int ScopedPort { get; } = GetFreePort();

            /// <summary>Binds real Kestrel on the two configured ports. Idempotent — safe to call from every test constructor.</summary>
            public void Start()
            {
                if (_started) return;
                _started = true;
                UseKestrel(); // real Kestrel, not TestServer — the app's DynamicApi section binds ManagementPort + ScopedPort
                CreateClient(); // force host startup (client discarded; tests address each port explicitly)
            }

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
                        // Overrides the appsettings.json sample endpoint so tests never bind 5101.
                        ["DynamicApi:ManagementPort"] = ManagementPort.ToString(),
                        ["DynamicApi:ListenAddress"] = "localhost",
                        ["DynamicApi:Endpoints:0:Port"] = ScopedPort.ToString(),
                        ["DynamicApi:Endpoints:0:NodePath"] = "ScopedOrg",
                    }));

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
            }

            private static int GetFreePort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
        }
    }
}
