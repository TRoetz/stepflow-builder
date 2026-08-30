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
