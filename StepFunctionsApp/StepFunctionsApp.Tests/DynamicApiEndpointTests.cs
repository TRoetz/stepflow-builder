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

        [Fact]
        public async Task Dispatch_EavGet_QueryLanguageAndLookup_MatchEngineSurface()
        {
            // Collection op on the base path + single-row lookup op; both must answer like /api/eav/{domain}/rows.
            await CreateApiAsync("E2E EAV Full API", "/eav-full", _domain,
                new { method = "GET", path = "", handlerType = "eav" },
                new { method = "POST", path = "", handlerType = "eav" },
                new { method = "DELETE", path = "/{rowKeyId}", handlerType = "eav" },
                new { method = "GET", path = "/{id}", handlerType = "eav" });

            var keys = new List<string>();
            try
            {
                foreach (var (entityId, name, qty) in new[] { ("e1", "alpha", 3), ("e2", "beta", 1), ("e1", "gamma", 2), ("solo", "delta", 9) })
                {
                    var (s, b) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/eav-full", new { entityId, entityType = "Gadget", name, qty });
                    Assert.Equal(System.Net.HttpStatusCode.Created, s);
                    keys.Add((string)b!["rowKeyId"]!);
                }

                // Sort desc + limit: count stays the post-filter total, page is sliced.
                var (s1, b1) = await SendAsync(_client, HttpMethod.Get, "/api/dynamic/eav-full?sort=-qty&limit=2", null);
                Assert.Equal(System.Net.HttpStatusCode.OK, s1);
                Assert.Equal(4, (int)b1!["count"]!);
                var page1 = b1["rows"]!.ToArray();
                Assert.Equal(2, page1.Length);
                Assert.Equal(9, (int)page1[0]!["values"]!["qty"]!); // qty 9 first when descending

                // Page 2 of limit-2 pages: two remaining rows.
                var (s2, b2) = await SendAsync(_client, HttpMethod.Get, "/api/dynamic/eav-full?page=2&limit=2", null);
                Assert.Equal(System.Net.HttpStatusCode.OK, s2);
                Assert.Equal(4, (int)b2!["count"]!);
                Assert.Equal(2, b2["rows"]!.ToArray().Length);

                // Field projection keeps rowKeyId and drops everything else.
                var (s3, b3) = await SendAsync(_client, HttpMethod.Get, "/api/dynamic/eav-full?fields=name", null);
                Assert.Equal(System.Net.HttpStatusCode.OK, s3);
                foreach (var row in b3!["rows"]!.Values<JObject>())
                {
                    var names = row.Properties().Select(p => p.Name).OrderBy(n => n).ToArray();
                    Assert.Equal(new[] { "name", "rowKeyId" }, names);
                    Assert.Null(row["values"]);
                }

                // Single-row lookup: unique entity id -> object.
                var (g1, r1) = await SendAsync(_client, HttpMethod.Get, "/api/dynamic/eav-full/solo", null);
                Assert.Equal(System.Net.HttpStatusCode.OK, g1);
                Assert.Equal(JTokenType.Object, r1!.Type);
                Assert.Equal("Gadget", (string)r1["entityType"]!);

                // Entity id matching several rows -> array of all matches.
                var (g2, r2) = await SendAsync(_client, HttpMethod.Get, "/api/dynamic/eav-full/e1", null);
                Assert.Equal(System.Net.HttpStatusCode.OK, g2);
                Assert.Equal(JTokenType.Array, r2!.Type);
                Assert.Equal(2, ((JArray)r2).Count);

                // No entity match -> RowKeyId fallback returns the row.
                var (g3, r3) = await SendAsync(_client, HttpMethod.Get, $"/api/dynamic/eav-full/{keys[0]}", null);
                Assert.Equal(System.Net.HttpStatusCode.OK, g3);
                Assert.Equal(keys[0], (string)r3!["rowKeyId"]!);

                // Unknown id -> 404 naming the id.
                var (g4, r4) = await SendAsync(_client, HttpMethod.Get, "/api/dynamic/eav-full/no-such-row", null);
                Assert.Equal(System.Net.HttpStatusCode.NotFound, g4);
                Assert.Contains("no-such-row", (string)r4!["error"]!, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                foreach (var key in keys)
                    await SendAsync(_client, HttpMethod.Delete, $"/api/dynamic/eav-full/{key}", null);
            }
        }

        [Fact]
        public async Task CreateApi_EavGet_TwoPathParams_Returns400()
        {
            var (status, body) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", new
            {
                name = "E2E EAV Two Params",
                nodePath = _org,
                basePath = "/eav-two-params",
                attributeDomain = _domain,
                operations = new[] { new { method = "GET", path = "/{a}/{b}", handlerType = "eav" } },
            });

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, status);
            Assert.Contains("at most one", (string)body!["error"]!, StringComparison.OrdinalIgnoreCase);
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

        // ── engine surface for DynamicApiHost ─────────────────────────────
        [Fact]
        public async Task AttributeDomain_GetByName_ReturnsEntry_AndUnknownIs404()
        {
            var (created, _) = await SendAsync(_client, HttpMethod.Post, "/api/attribute-domains", new { attributeDomain = new { attributeDomainName = _domain } });
            Assert.Equal(System.Net.HttpStatusCode.OK, created);

            var (s1, b1) = await SendAsync(_client, HttpMethod.Get, $"/api/attribute-domains/{_domain}", null);
            Assert.Equal(System.Net.HttpStatusCode.OK, s1);
            Assert.Equal(_domain, (string)b1!["attributeDomain"]!["attributeDomainName"]!);

            var (s2, b2) = await SendAsync(_client, HttpMethod.Get, "/api/attribute-domains/no-such-domain-xyz", null);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, s2);
        }

        [Fact]
        public async Task ExecuteSync_UnknownFlow_Returns404()
        {
            var id = "no-such-flow-" + Guid.NewGuid().ToString("N");
            var (status, body) = await SendAsync(_client, HttpMethod.Post, $"/api/flows/execute-sync/{id}", new { });
            Assert.Equal(System.Net.HttpStatusCode.NotFound, status);
            Assert.Contains(id, (string)body!["error"]!, StringComparison.OrdinalIgnoreCase);
        }

        // ── published flag (external host exposure) ───────────────────────
        [Fact]
        public async Task PublishedFlag_RoundTrips_AndFiltersList()
        {
            var payload = new Dictionary<string, object?>
            {
                ["name"] = "E2E Publish API",
                ["nodePath"] = _org,
                ["basePath"] = "/publish-test",
                ["isPublished"] = true,
                ["operations"] = new[] { new { method = "GET", path = "", handlerType = "flow", flowId = "any-flow" } },
            };
            var (created, body) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", payload);
            Assert.Equal(System.Net.HttpStatusCode.Created, created);
            var id = (string)body!["id"]!;

            // Wire shape round-trips as camelCase isPublished.
            var (get1, g1) = await SendAsync(_client, HttpMethod.Get, $"/api/dynamic/apis/{id}", null);
            Assert.Equal(System.Net.HttpStatusCode.OK, get1);
            Assert.True((bool)g1!["isPublished"]!);

            // published=true contains it; published=false does not.
            var (s1, b1) = await SendAsync(_client, HttpMethod.Get, $"/api/dynamic/apis?nodePath={_org}&published=true", null);
            Assert.Contains((JArray)b1!, r => string.Equals((string?)r!["id"], id, StringComparison.Ordinal));

            var (s2, b2) = await SendAsync(_client, HttpMethod.Get, $"/api/dynamic/apis?nodePath={_org}&published=false", null);
            Assert.DoesNotContain((JArray)b2!, r => string.Equals((string?)r!["id"], id, StringComparison.Ordinal));

            // Unpublish via re-save (same name+nodePath reuses the id).
            payload["isPublished"] = false;
            var (updated, _) = await SendAsync(_client, HttpMethod.Post, "/api/dynamic/apis", payload);
            Assert.Equal(System.Net.HttpStatusCode.OK, updated);

            var (s3, b3) = await SendAsync(_client, HttpMethod.Get, $"/api/dynamic/apis?nodePath={_org}&published=false", null);
            Assert.Contains((JArray)b3!, r => string.Equals((string?)r!["id"], id, StringComparison.Ordinal));

            var (s4, b4) = await SendAsync(_client, HttpMethod.Get, $"/api/dynamic/apis?nodePath={_org}&published=true", null);
            Assert.DoesNotContain((JArray)b4!, r => string.Equals((string?)r!["id"], id, StringComparison.Ordinal));
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
