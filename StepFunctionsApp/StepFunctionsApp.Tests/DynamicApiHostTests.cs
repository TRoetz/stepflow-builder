using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DynamicApi.Host;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Dynamic API host tests — the standalone external service (DynamicApiHost) with its backend engine
    /// stubbed by a fake HttpMessageHandler. Verifies catalog sync + /health, and that dynamic routes forward
    /// to the engine's REST surface (flows -> api/flows/execute-sync/{id}, dataExchange -> api/data-exchange/execute)
    /// with status/body pass-through and flow 200 responses mapped by their `status` field.
    /// </summary>
    public sealed class DynamicApiHostTests : IClassFixture<DynamicApiHostTests.Factory>, IAsyncLifetime, IDisposable
    {
        private readonly Factory _factory;
        private readonly HttpClient _client;

        public DynamicApiHostTests(Factory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        public Task InitializeAsync()
        {
            // Fresh stub state per test: the fixture (and its engine host) is shared across the class.
            _factory.Engine.Reset();
            return Task.CompletedTask;
        }


        public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }
        public void Dispose() => _client.Dispose();

        private StubEngineHandler Engine => _factory.Engine;

        /// <summary>Loads a one-API catalog into the host and returns it (deterministic — no polling wait).</summary>
        private async Task SetCatalogAsync(string operationsJson)
        {
            Engine.CatalogJson = new JArray
            {
                new JObject
                {
                    ["id"] = "api-1",
                    ["name"] = "Host Test API",
                    ["nodePath"] = "/org",
                    ["basePath"] = "/orders",
                    ["isActive"] = true,
                    ["published"] = true,
                    ["operations"] = JArray.Parse(operationsJson),
                },
            }.ToString(Newtonsoft.Json.Formatting.None);

            await _factory.Services.GetRequiredService<CatalogService>().RefreshAsync();
        }

        private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object payload) =>
            client.PostAsync(url, new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

        [Fact]
        public async Task Health_Ready_AfterCatalogSync()
        {
            await SetCatalogAsync("[{ \"method\": \"POST\", \"path\": \"\", \"handlerType\": \"flow\", \"flowId\": \"f-1\" }]");

            var res = await _client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = JToken.Parse(await res.Content.ReadAsStringAsync());
            Assert.Equal("ready", (string)body["status"]!);
            Assert.Equal(1, (int)body["apisLoaded"]!);
        }

        [Fact]
        public async Task FlowHandler_ForwardsToExecuteSync_AndMapsSucceededOutput()
        {
            await SetCatalogAsync("[{ \"method\": \"POST\", \"path\": \"\", \"handlerType\": \"flow\", \"flowId\": \"f-1\" }]");
            Engine.Enqueue(HttpStatusCode.OK, "{\"status\":\"Succeeded\",\"output\":{\"ok\":true}}");

            var res = await PostAsync(_client, "/api/dynamic/orders", new { qty = 2 });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            // Succeeded -> the engine's `output` value becomes the response body.
            Assert.Equal(JToken.Parse("{\"ok\":true}"), JToken.Parse(await res.Content.ReadAsStringAsync()));

            var (method, uri, sentBody) = Engine.LastRequest;
            Assert.Equal("POST", method);
            Assert.Equal("http://stub-engine.local/api/flows/execute-sync/f-1", uri.ToString());
            // Merged input: request body + query + path params.
            Assert.Equal(JToken.Parse("{\"qty\":2}"), JToken.Parse(sentBody));
        }

        [Fact]
        public async Task FlowHandler_MapsFailedStatusTo500()
        {
            await SetCatalogAsync("[{ \"method\": \"POST\", \"path\": \"\", \"handlerType\": \"flow\", \"flowId\": \"f-1\" }]");
            Engine.Enqueue(HttpStatusCode.OK, "{\"status\":\"Failed\",\"errorCode\":\"States.Failed\",\"errorMessage\":\"boom\"}");

            var res = await PostAsync(_client, "/api/dynamic/orders", new { qty = 2 });
            Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
            var body = JToken.Parse(await res.Content.ReadAsStringAsync());
            Assert.Equal("States.Failed", (string)body["error"]!);
            Assert.Equal("boom", (string)body["message"]!);
        }

        [Fact]
        public async Task DataExchangeHandler_ForwardsProfile_AndPassesThroughEngineError()
        {
            await SetCatalogAsync("[{ \"method\": \"POST\", \"path\": \"\", \"handlerType\": \"dataExchange\", \"profileId\": \"p-1\" }]");
            Engine.Enqueue(HttpStatusCode.UnprocessableEntity, "{\"error\":\"bad input\"}");

            var res = await PostAsync(_client, "/api/dynamic/orders", new { a = 1 });
            // Non-200 engine responses pass through unchanged (status + body).
            Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
            Assert.Equal(JToken.Parse("{\"error\":\"bad input\"}"), JToken.Parse(await res.Content.ReadAsStringAsync()));

            var (method, uri, sentBody) = Engine.LastRequest;
            Assert.Equal("POST", method);
            Assert.Equal("http://stub-engine.local/api/data-exchange/execute", uri.ToString());
            var payload = JToken.Parse(sentBody);
            Assert.Equal("p-1", (string)payload["profileId"]!);
            Assert.Equal(JToken.Parse("{\"a\":1}"), payload["input"]);
        }

        [Fact]
        public async Task UnknownPath_Returns404_WithoutCallingEngine()
        {
            await SetCatalogAsync("[{ \"method\": \"POST\", \"path\": \"\", \"handlerType\": \"flow\", \"flowId\": \"f-1\" }]");

            var res = await _client.GetAsync("/api/dynamic/nope");
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            var body = JToken.Parse(await res.Content.ReadAsStringAsync());
            Assert.Contains("No dynamic API matches", (string)body["error"]!);
            Assert.Empty(Engine.Requests); // matching is local; the engine is never contacted
        }

        // ── test host factory: stubbed engine handler + fixed base URL ────────────────

        public sealed class Factory : WebApplicationFactory<HostProgram>
        {
            public StubEngineHandler Engine { get; } = new();

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Engine:BaseUrl"] = "http://stub-engine.local/",
                        ["Sync:IntervalSeconds"] = "30", // keep background polling quiet; tests refresh explicitly
                    }));

                builder.ConfigureServices(services => services
                    .AddHttpClient(EngineOptions.ClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Engine));
            }
        }

        /// <summary>
        /// Fake backend engine: serves the published-API catalog (GET /api/dynamic/apis?published=true) from
        /// <see cref="CatalogJson"/> and records every other request, answering from a FIFO queue of canned responses.
        /// </summary>
        public sealed class StubEngineHandler : HttpMessageHandler
        {
            private readonly object _gate = new();
            private readonly Queue<HttpResponseMessage> _responses = new();

            /// <summary>JSON served for the catalog pull.</summary>
            public string CatalogJson { get; set; } = "[]";

            /// <summary>All non-catalog engine requests in order: (method, absolute URI, body).</summary>
            public List<(string Method, Uri Uri, string Body)> Requests { get; } = new();

            public (string Method, Uri Uri, string Body) LastRequest
            {
                get
                {
                    lock (_gate) return Requests[^1];
                }
            }

            public void Enqueue(HttpStatusCode status, string json)
            {
                lock (_gate) _responses.Enqueue(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }

            /// <summary>Drops queued responses and recorded requests (per-test isolation).</summary>
            public void Reset()
            {
                lock (_gate)
                {
                    _responses.Clear();
                    Requests.Clear();
                }
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
                var uri = request.RequestUri!;

                if (request.Method == HttpMethod.Get && uri.AbsolutePath == "/api/dynamic/apis" && uri.Query.Contains("published=true"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CatalogJson, Encoding.UTF8, "application/json") };

                lock (_gate)
                {
                    Requests.Add((request.Method.Method, uri, body));
                    if (_responses.Count > 0) return _responses.Dequeue();
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }
        }
    }
}
