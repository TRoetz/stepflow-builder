using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.StepFunctions;
using StepFunctionsApp.Workspace;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Dynamic API endpoint tests - the real /api/dynamic surface (management CRUD + dispatcher)
    /// over WebApplicationFactory with isolated flow-state, SQLite db, workspace and data-exchange dirs.
    /// All tests share one factory; each test uses a unique API name + basePath so route-conflict
    /// validation never fires between tests.
    /// </summary>
    public sealed class DynamicApiEndpointTests : IClassFixture<DynamicApiEndpointTests.Factory>
    {
        private readonly Factory _factory;
        private readonly HttpClient _client;
        private readonly WorkspaceStore _workspace;
        private readonly string _org;
        private readonly string _domain = "E2EDomain-" + Guid.NewGuid().ToString("N");

        public DynamicApiEndpointTests(Factory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
            _workspace = factory.Services.GetRequiredService<WorkspaceStore>();
            _org = _workspace.CreateNode("E2EOrg"); // idempotent across tests (shared fixture)
        }

        private static async Task<(HttpStatusCode Status, JToken Body)> SendAsync(HttpClient client, HttpMethod method, string url, object? payload)
        {
            var content = payload == null ? null : new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var res = await client.SendAsync(new HttpRequestMessage(method, url) { Content = content });
            var text = await res.Content.ReadAsStringAsync();
            return (res.StatusCode, string.IsNullOrWhiteSpace(text) ? JValue.CreateNull() : JToken.Parse(text));
        }

        private async Task<JObject> CreateApiAsync(string name, string basePath, object? attributeDomain, params object[] operations)
        {
            var payload = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["nodePath"] = _org,
                ["basePath"] = basePath,
                ["operations"] = operations,
            };
            if (attributeDomain != null) payload["attributeDomain"] = attributeDomain;

            var (status, body) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", payload);
            Assert.Equal(System.Net.HttpStatusCode.Created, status);
            return (JObject)body!;
        }

        // ── management CRUD ────────────────────────────────────────────────

        [Fact]
        public async Task CreateApi_Returns201_AndAppearsInList()
        {
            var body = await CreateApiAsync("E2E Create API", "/create-test", null,
                new { method = "GET", path = "", handlerType = "flow", flowId = "any-flow" });

            Assert.False(string.IsNullOrEmpty((string)body["id"]!));
            Assert.True((bool)body["created"]!);
            var id = (string)body["id"]!;

            var (listStatus, listBody) = await SendAsync(_client, HttpMethod.Get, $"/api/dynamic/apis?nodePath={_org}", null);
            Assert.Equal(System.Net.HttpStatusCode.OK, listStatus);
            Assert.Contains((JArray)listBody!, r => (string)r!["id"] == id);

            var (delStatus, delBody) = await SendAsync(_client, HttpMethod.Delete, $"/api/dynamic/apis/{id}", null);
            Assert.Equal(System.Net.HttpStatusCode.OK, delStatus);
            Assert.True((bool)delBody!["deleted"]!);
        }

        [Fact]
        public async Task CreateApi_ValidationErrors_Return400Or404()
        {
            // Missing name -> 400.
            var (s1, b1) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", new { nodePath = _org, basePath = "/v1" });
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, s1);
            Assert.Contains("name", (string)b1!["error"]!, StringComparison.OrdinalIgnoreCase);

            // Unknown workspace node -> 404.
            var (s2, b2) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", new { name = "E2E Bad Node", nodePath = "No/Such/Org", basePath = "/v2" });
            Assert.Equal(System.Net.HttpStatusCode.NotFound, s2);

            // Invalid node segment -> 400.
            var (s3, _) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", new { name = "E2E Bad Seg", nodePath = "bad*seg", basePath = "/v3" });
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, s3);

            // eav operation without a domain -> 400.
            var (s4, b4) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", new { name = "E2E No Domain", nodePath = _org, basePath = "/v4", operations = new[] { new { method = "GET", path = "", handlerType = "eav" } } });
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, s4);
            Assert.Contains("domain", (string)b4!["error"]!, StringComparison.OrdinalIgnoreCase);

            // Unknown handler type -> 400.
            var (s5, _) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", new { name = "E2E Bad Handler", nodePath = _org, basePath = "/v5", operations = new[] { new { method = "GET", path = "", handlerType = "teleport" } } });
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, s5);
        }

        // ── eav handler round trip ─────────────────────────────────────────

        [Fact]
        public async Task Dispatch_EavHandler_FullCrudRoundTrip()
        {
            await CreateApiAsync("E2E EAV API", "/eav-rows", _domain,
                new { method = "GET", path = "", handlerType = "eav" },
                new { method = "POST", path = "", handlerType = "eav" },
                new { method = "PUT", path = "/{rowKeyId}", handlerType = "eav" },
                new { method = "PATCH", path = "/{rowKeyId}", handlerType = "eav" },
                new { method = "DELETE", path = "/{rowKeyId}", handlerType = "eav" });

            // POST a row.
            var (s1, b1) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/eav-rows", new { entityId = "e1", entityType = "Widget", name = "alpha" });
            Assert.Equal(System.Net.HttpStatusCode.Created, s1);
            var key = (string)b1!["rowKeyId"]!;
            Assert.False(string.IsNullOrEmpty(key));

            // GET with entity filter.
            var (s2, b2) = await SendAsync(_client, HttpMethod.Get, "/api/dynamic/eav-rows?entityId=e1", null);
            Assert.Equal(System.Net.HttpStatusCode.OK, s2);
            Assert.Equal(1, (int)b2!["count"]!);
            Assert.Equal("alpha", (string)b2["rows"]![0]!["values"]!["name"]!);

            // PATCH merges.
            var (s3, b3) = await SendAsync(_client, HttpMethod.Patch, $"/api/dynamic/eav-rows/{key}", new { name = "beta" });
            Assert.Equal(System.Net.HttpStatusCode.OK, s3);
            Assert.Equal("beta", (string)b3!["values"]!["name"]!);
            Assert.Equal("Widget", (string)b3["entityType"]!); // preserved by merge

            // PUT replaces values wholesale.
            var (s4, b4) = await SendAsync(_client, HttpMethod.Put, $"/api/dynamic/eav-rows/{key}", new { note = "replaced" });
            Assert.Equal(System.Net.HttpStatusCode.OK, s4);
            Assert.Null(b4!["values"]!["name"]);
            Assert.Equal("replaced", (string)b4["values"]!["note"]!);

            // DELETE.
            var (s5, b5) = await SendAsync(_client, HttpMethod.Delete, $"/api/dynamic/eav-rows/{key}", null);
            Assert.Equal(System.Net.HttpStatusCode.OK, s5);
            Assert.Equal("deleted", (string)b5!["status"]!);

            var (s6, b6) = await SendAsync(_client, HttpMethod.Get, "/api/dynamic/eav-rows?entityId=e1", null);
            Assert.Equal(0, (int)b6!["count"]!);
        }

        // ── bearer auth ────────────────────────────────────────────────────

        [Fact]
        public async Task Dispatch_EnforcesBearerToken()
        {
            var payload = new Dictionary<string, object?>
            {
                ["name"] = "E2E Auth API",
                ["nodePath"] = _org,
                ["basePath"] = "/authed-rows",
                ["attributeDomain"] = _domain,
                ["bearerToken"] = "s3cret",
                ["operations"] = new[] { new { method = "GET", path = "", handlerType = "eav" } },
            };
            var (created, _) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", payload);
            Assert.Equal(System.Net.HttpStatusCode.Created, created);

            // No token -> 401 with WWW-Authenticate.
            var res1 = await _client.GetAsync("/api/dynamic/authed-rows");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, res1.StatusCode);
            Assert.NotNull(res1.Headers.WwwAuthenticate);

            // Wrong token -> 401.
            var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/dynamic/authed-rows");
            req2.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await _client.SendAsync(req2)).StatusCode);

            // Correct token -> 200 with rows.
            var req3 = new HttpRequestMessage(HttpMethod.Get, "/api/dynamic/authed-rows");
            req3.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "s3cret");
            var res3 = await _client.SendAsync(req3);
            Assert.Equal(System.Net.HttpStatusCode.OK, res3.StatusCode);
            var body3 = JToken.Parse(await res3.Content.ReadAsStringAsync());
            Assert.NotNull(body3["rows"]);
        }

        // ── 404 / 405 routing ──────────────────────────────────────────────

        [Fact]
        public async Task Dispatch_NoMatch_Returns404_AndMethodMismatch_Returns405WithAllow()
        {
            await CreateApiAsync("E2E Method API", "/method-test", _domain,
                new { method = "GET", path = "", handlerType = "eav" });

            // Unknown path -> 404 with error message.
            var (s1, b1) = await SendAsync(_client, HttpMethod.Get, "/api/dynamic/unknown-xyz", null);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, s1);
            Assert.False(string.IsNullOrEmpty((string)b1!["error"]!));

            // Method mismatch -> 405 with Allow header.
            var res2 = await _client.PostAsync("/api/dynamic/method-test", new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, res2.StatusCode);
            Assert.Contains("GET", string.Join(",", res2.Content.Headers.Allow));
        }

        // ── flow handler ───────────────────────────────────────────────────

        [Fact]
        public async Task Dispatch_FlowHandler_ExecutesRegisteredFlow_AndUnknownFlowIs404()
        {
            var stepService = _factory.Services.GetRequiredService<StepFunctionService>();
            var states = JObject.Parse(
                "{\"Start\":{\"type\":\"Pass\",\"next\":\"Echo\"}," +
                "\"Echo\":{\"type\":\"Task\",\"resource\":\"internal://echo\",\"parameters\":{\"note\":\"hello from dynamic api\"},\"next\":\"Done\"}," +
                "\"Done\":{\"type\":\"Succeed\"}}");
            stepService.RegisterStateMachine("e2e-dyn-flow", new StateMachineDefinition
            {
                StartAt = "Start",
                States = states.ToObject<Dictionary<string, StateDefinition>>()!,
            }, id: "e2e-dyn-flow");

            await CreateApiAsync("E2E Flow API", "/flow-test", null,
                new { method = "POST", path = "", handlerType = "flow", flowId = "e2e-dyn-flow" });

            var (s1, b1) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/flow-test", null);
            Assert.Equal(System.Net.HttpStatusCode.OK, s1);
            Assert.Equal("hello from dynamic api", (string)b1!["note"]!);

            // Unknown flow id -> 404 naming the flow.
            await CreateApiAsync("E2E Flow Missing API", "/flow-missing", null,
                new { method = "POST", path = "", handlerType = "flow", flowId = "no-such-flow-e2e" });

            var (s2, b2) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/flow-missing", null);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, s2);
            Assert.Contains("no-such-flow-e2e", (string)b2!["error"]!, StringComparison.OrdinalIgnoreCase);
        }

        // ── OpenAPI spec endpoint ──────────────────────────────────────────

        [Fact]
        public async Task OpenApiEndpoint_DescribesActiveApis_WithSecurityWhenTokened()
        {
            var payload = new Dictionary<string, object?>
            {
                ["name"] = "E2E Spec API",
                ["nodePath"] = _org,
                ["basePath"] = "/spec-test",
                ["attributeDomain"] = _domain,
                ["bearerToken"] = "s3cret",
                ["operations"] = new[] { new { method = "GET", path = "", handlerType = "eav" } },
            };
            var (created, _) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", payload);
            Assert.Equal(System.Net.HttpStatusCode.Created, created);

            var res = await _client.GetAsync("/api/dynamic/openapi.json");
            Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
            var doc = JObject.Parse(await res.Content.ReadAsStringAsync());

            Assert.Equal("3.0.1", (string)doc["openapi"]!);
            Assert.NotNull(doc["paths"]!["/spec-test"]!["get"]);
            Assert.NotNull(doc["components"]!["securitySchemes"]!["bearerAuth"]); // this API is tokened
            Assert.NotNull(doc["components"]!["schemas"]!["EavRow"]);
        }

        // ── test host factory (isolated state dirs) ────────────────────────

        public sealed class Factory : WebApplicationFactory<Program>
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "stepflow-dynamic-api-tests", Guid.NewGuid().ToString("N"));

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
                    }));

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
