using System;
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
    /// EAV rows endpoint tests — CRUD round trip against /api/eav/{domain}/rows, the engine surface that external
    /// Dynamic API hosts forward eav operations to. Wire shape mirrors the in-process dispatcher's eav handler:
    /// top-level entityId/entityType become row fields, everything else is captured values; list returns { rows, count }.
    /// </summary>
    public sealed class EavRowsEndpointTests : IClassFixture<EavRowsEndpointTests.Factory>
    {
        private readonly HttpClient _client;

        // Unique domain per test run: rows persist to eav-data/{domain}.json under the process CWD (shared with other suites).
        private static readonly string _domain = "EAVCRUD-" + Guid.NewGuid().ToString("N");

        public EavRowsEndpointTests(Factory factory)
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

        [Fact]
        public async Task Crud_FullRoundTrip()
        {
            // POST a row: top-level entityId/entityType become row fields; the rest is captured values.
            var (s1, b1) = await SendAsync(_client, HttpMethod.Post, $"/api/eav/{_domain}/rows", new { entityId = "e1", entityType = "Widget", name = "alpha" });
            Assert.Equal(HttpStatusCode.Created, s1);
            var key = (string)b1!["rowKeyId"]!;
            Assert.False(string.IsNullOrEmpty(key));
            Assert.Equal("e1", (string)b1["entityId"]!);
            Assert.Equal("Widget", (string)b1["entityType"]!);
            Assert.Equal("alpha", (string)b1["values"]!["name"]!);

            // GET list with entity filter.
            var (s2, b2) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows?entityId=e1", null);
            Assert.Equal(HttpStatusCode.OK, s2);
            Assert.Equal(1, (int)b2!["count"]!);
            Assert.Equal("alpha", (string)b2["rows"]![0]!["values"]!["name"]!);

            // PATCH merges; existing keys preserved.
            var (s3, b3) = await SendAsync(_client, HttpMethod.Patch, $"/api/eav/{_domain}/rows/{key}", new { name = "beta" });
            Assert.Equal(HttpStatusCode.OK, s3);
            Assert.Equal("beta", (string)b3!["values"]!["name"]!);

            // PUT replaces values wholesale.
            var (s4, b4) = await SendAsync(_client, HttpMethod.Put, $"/api/eav/{_domain}/rows/{key}", new { note = "replaced" });
            Assert.Equal(HttpStatusCode.OK, s4);
            Assert.Null(b4!["values"]!["name"]);
            Assert.Equal("replaced", (string)b4["values"]!["note"]!);

            // DELETE; a second delete is 404.
            var (s5, b5) = await SendAsync(_client, HttpMethod.Delete, $"/api/eav/{_domain}/rows/{key}", null);
            Assert.Equal(HttpStatusCode.OK, s5);
            Assert.Equal("deleted", (string)b5!["status"]!);

            var (s6, _) = await SendAsync(_client, HttpMethod.Delete, $"/api/eav/{_domain}/rows/{key}", null);
            Assert.Equal(HttpStatusCode.NotFound, s6);

            // List is empty again.
            var (s7, b7) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows", null);
            Assert.Equal(0, (int)b7!["count"]!);
        }

        [Fact]
        public async Task UnknownRow_Returns404_AndUnsafeDomainReturns400()
        {
            // Update/delete of a missing row -> 404.
            var (s1, b1) = await SendAsync(_client, HttpMethod.Put, $"/api/eav/{_domain}/rows/no-such-row", new { name = "x" });
            Assert.Equal(HttpStatusCode.NotFound, s1);
            Assert.Contains("no-such-row", (string)b1!["error"]!, StringComparison.OrdinalIgnoreCase);

            var (s2, _) = await SendAsync(_client, HttpMethod.Delete, $"/api/eav/{_domain}/rows/no-such-row", null);
            Assert.Equal(HttpStatusCode.NotFound, s2);

            // Unsafe domain name -> 400 from the store's SafeDomainRegex.
            var (s3, b3) = await SendAsync(_client, HttpMethod.Get, "/api/eav/bad*seg/rows", null);
            Assert.Equal(HttpStatusCode.BadRequest, s3);
            Assert.NotNull(b3!["error"]);
        }

        [Fact]
        public async Task List_FullQueryLanguage_FilterSortPageLimitOffsetFields()
        {
            // Seed three rows (entityId e1/e2/e1, name alpha/beta/gamma, qty 3/1/2); removed again so the shared domain stays empty.
            var keys = new List<string>();
            try
            {
                foreach (var (entityId, name, qty) in new[] { ("e1", "alpha", 3), ("e2", "beta", 1), ("e1", "gamma", 2) })
                {
                    var (s, b) = await SendAsync(_client, HttpMethod.Post, $"/api/eav/{_domain}/rows", new { entityId, entityType = "Gadget", name, qty });
                    Assert.Equal(HttpStatusCode.Created, s);
                    keys.Add((string)b!["rowKeyId"]!);
                }

                // Filter by a captured Values key; AND across different keys; OR across repeated keys.
                var (s1, b1) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows?name=alpha&entityType=Gadget", null);
                Assert.Equal(HttpStatusCode.OK, s1);
                Assert.Equal(1, (int)b1!["count"]!);
                Assert.Equal("alpha", (string)b1["rows"]![0]!["values"]!["name"]!);

                var (s2, b2) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows?name=alpha&name=gamma", null);
                Assert.Equal(HttpStatusCode.OK, s2);
                Assert.Equal(2, (int)b2!["count"]!);

                // Sort desc by a Values key with limit: count stays the post-filter total, page is sliced.
                var (s3, b3) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows?sort=-qty&limit=2", null);
                Assert.Equal(HttpStatusCode.OK, s3);
                Assert.Equal(3, (int)b3!["count"]!);
                var page3 = b3["rows"]!.ToArray();
                Assert.Equal(2, page3.Length);
                Assert.Equal(3, (int)page3[0]!["values"]!["qty"]!); // qty 3 first when descending

                // Page 2 of limit-2 pages: the single remaining row.
                var (s4, b4) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows?page=2&limit=2", null);
                Assert.Equal(HttpStatusCode.OK, s4);
                Assert.Equal(3, (int)b4!["count"]!);
                Assert.Single(b4["rows"]!.ToArray());

                // Field projection keeps rowKeyId for CRUD addressing and drops everything else.
                var (s5, b5) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows?fields=name", null);
                Assert.Equal(HttpStatusCode.OK, s5);
                foreach (var row in b5!["rows"]!.Values<JObject>())
                {
                    var names = row.Properties().Select(p => p.Name).OrderBy(n => n).ToArray();
                    Assert.Equal(new[] { "name", "rowKeyId" }, names);
                    Assert.Null(row["values"]);
                }

                // page + offset together -> 400.
                var (s6, b6) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows?page=1&offset=5", null);
                Assert.Equal(HttpStatusCode.BadRequest, s6);
                Assert.Contains("page", (string)b6!["error"]!, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                foreach (var key in keys)
                    await SendAsync(_client, HttpMethod.Delete, $"/api/eav/{_domain}/rows/{key}", null);
            }
        }

        [Fact]
        public async Task Get_SingleRowLookup_EntityIdFirst_RowKeyIdFallback_MultipleArray_Unknown404()
        {
            var keys = new List<string>();
            try
            {
                // Two rows share entityId "dup"; one is unique; all are removed afterwards.
                var (s1, b1) = await SendAsync(_client, HttpMethod.Post, $"/api/eav/{_domain}/rows", new { entityId = "dup", entityType = "Gadget", name = "one" });
                Assert.Equal(HttpStatusCode.Created, s1);
                keys.Add((string)b1!["rowKeyId"]!);

                var (s2, b2) = await SendAsync(_client, HttpMethod.Post, $"/api/eav/{_domain}/rows", new { entityId = "dup", entityType = "Gadget", name = "two" });
                Assert.Equal(HttpStatusCode.Created, s2);
                keys.Add((string)b2!["rowKeyId"]!);

                var (s3, b3) = await SendAsync(_client, HttpMethod.Post, $"/api/eav/{_domain}/rows", new { entityId = "solo", entityType = "Gadget", name = "three" });
                Assert.Equal(HttpStatusCode.Created, s3);
                keys.Add((string)b3!["rowKeyId"]!);

                // Unique entity id -> a single object.
                var (g1, r1) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows/solo", null);
                Assert.Equal(HttpStatusCode.OK, g1);
                Assert.Equal(JTokenType.Object, r1!.Type);
                Assert.Equal("Gadget", (string)r1["entityType"]!);

                // Entity id matching several rows -> an array of all matches.
                var (g2, r2) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows/dup", null);
                Assert.Equal(HttpStatusCode.OK, g2);
                Assert.Equal(JTokenType.Array, r2!.Type);
                Assert.Equal(2, ((JArray)r2).Count);

                // No entity match -> RowKeyId fallback returns the row.
                var (g3, r3) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows/{keys[0]}", null);
                Assert.Equal(HttpStatusCode.OK, g3);
                Assert.Equal(keys[0], (string)r3!["rowKeyId"]!);

                // Unknown id -> 404 naming the id.
                var (g4, r4) = await SendAsync(_client, HttpMethod.Get, $"/api/eav/{_domain}/rows/no-such-row", null);
                Assert.Equal(HttpStatusCode.NotFound, g4);
                Assert.Contains("no-such-row", (string)r4!["error"]!, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                foreach (var key in keys)
                    await SendAsync(_client, HttpMethod.Delete, $"/api/eav/{_domain}/rows/{key}", null);
            }
        }

        // ── test host factory (isolated state dirs; eav-data stays CWD-relative like in production) ──

        public sealed class Factory : WebApplicationFactory<Program>
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "stepflow-eav-rows-tests", Guid.NewGuid().ToString("N"));

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
