using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Tests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // AI PROXY ENDPOINT TESTS
    // Exercises /api/ai/chat and /api/ai/models in-process via WebApplicationFactory,
    // with a real loopback HttpListener standing in for the local LLM server (LM Studio
    // wire shape). The proxy exists so browsers can reach remote LAN endpoints that do
    // not send CORS headers — these tests verify forwarding, URL normalization and the
    // error contract end to end.
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class AiProxyEndpointTests : IClassFixture<AiProxyEndpointTests.Factory>
    {
        public sealed class Factory : WebApplicationFactory<Program>
        {
            // Isolate this host's durable flow-state store in a fresh temp dir so startup recovery never loads
            // checkpoints from other runs.
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

        private readonly HttpClient _client;

        public AiProxyEndpointTests(Factory factory) => _client = factory.CreateClient();

        // ── Fake upstream LLM server (loopback HttpListener) ────────────────

        /// <summary>Stands in for a local LLM endpoint: records requests and serves canned OpenAI-compatible responses.</summary>
        private sealed class FakeUpstream : IAsyncDisposable
        {
            public string BaseUrl { get; }
            public JObject? LastChatBody { get; private set; }
            public string? LastPath { get; private set; }

            /// <summary>Per-test override for the canned response; null uses the default catalog.</summary>
            public Func<string, (int Status, string Body)>? Responder { get; set; }

            private readonly HttpListener _listener = new();
            private readonly Task _loop;

            public FakeUpstream()
            {
                BaseUrl = $"http://127.0.0.1:{GetFreePort()}";
                _listener.Prefixes.Add($"{BaseUrl}/");
                _listener.Start();
                _loop = Task.Run(LoopAsync);
            }

            public static int GetFreePort()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                return port;
            }

            private async Task LoopAsync()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); }
                    catch { break; } // listener stopped
                    _ = Task.Run(() => HandleAsync(context));
                }
            }

            private async Task HandleAsync(HttpListenerContext context)
            {
                try
                {
                    LastPath = context.Request.Url?.AbsolutePath;
                    var bodyText = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
                    if (context.Request.HttpMethod == "POST" && !string.IsNullOrWhiteSpace(bodyText))
                        LastChatBody = JObject.Parse(bodyText);

                    var path = context.Request.Url!.AbsolutePath;
                    var (status, response) = Responder?.Invoke(path) ?? DefaultResponse(path);

                    var buffer = Encoding.UTF8.GetBytes(response);
                    context.Response.StatusCode = status;
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(buffer);
                    context.Response.Close();
                }
                catch { /* connection dropped — ignore */ }
            }

            private static (int, string) DefaultResponse(string path) =>
                path.EndsWith("/v1/chat/completions")
                    ? (200, """{"choices":[{"message":{"role":"assistant","content":"hello from upstream"},"finish_reason":"stop"}]}""")
                    : path.EndsWith("/v1/models")
                        ? (200, """{"data":[{"id":"qwen/qwen3-8b"},{"id":"gemma-4-e2b-it"}],"object":"list"}""")
                        : path.EndsWith("/api/tags")
                            ? (200, """{"models":[{"name":"llama3.1:8b"},{"name":"mistral"}]}""")
                            : (404, """{"error":{"message":"not found"}}""");

            public async ValueTask DisposeAsync()
            {
                _listener.Stop();
                try { await _loop; } catch { /* best effort */ }
            }
        }

        // ── /api/ai/chat ─────────────────────────────────────────────────────

        [Fact]
        public async Task Chat_ForwardsToConfiguredLocalEndpoint_AndReturnsUpstreamBody()
        {
            await using var upstream = new FakeUpstream();

            var response = await _client.PostAsJsonAsync("/api/ai/chat", new
            {
                provider = "lmStudio",
                baseUrl = upstream.BaseUrl,
                model = "qwen/qwen3-8b",
                messages = new[] { new { role = "user", content = "Hi" } },
                maxTokens = 100,
                temperature = 0.7
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("hello from upstream", (string)json["choices"]![0]!["message"]!["content"]);

            // The relay hit the configured endpoint with an OpenAI-compatible payload.
            Assert.EndsWith("/v1/chat/completions", upstream.LastPath);
            Assert.Equal("qwen/qwen3-8b", (string)upstream.LastChatBody!["model"]);
            Assert.Equal(100, (int)upstream.LastChatBody["max_tokens"]!);
        }

        [Fact]
        public async Task Chat_NormalizesBaseUrlWithTrailingV1()
        {
            await using var upstream = new FakeUpstream();

            var response = await _client.PostAsJsonAsync("/api/ai/chat", new
            {
                provider = "lmStudio",
                baseUrl = $"{upstream.BaseUrl}/v1/",
                model = "qwen/qwen3-8b",
                messages = new[] { new { role = "user", content = "Hi" } }
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // Trailing /v1 must be stripped — the path is /v1/chat/completions, not /v1/v1/...
            Assert.EndsWith("/v1/chat/completions", upstream.LastPath);
        }

        [Fact]
        public async Task Chat_Returns502WithUpstreamMessage_WhenModelNotFound()
        {
            await using var upstream = new FakeUpstream
            {
                Responder = _ => (404, """{"error":{"message":"model 'llama3.3' not found"}}""")
            };

            var response = await _client.PostAsJsonAsync("/api/ai/chat", new
            {
                provider = "lmStudio",
                baseUrl = upstream.BaseUrl,
                model = "llama3.3",
                messages = new[] { new { role = "user", content = "Hi" } }
            });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains("not found", (string)json["error"]!["message"]);
        }

        [Fact]
        public async Task Chat_RejectsNonLocalProvider()
        {
            await using var upstream = new FakeUpstream();

            var response = await _client.PostAsJsonAsync("/api/ai/chat", new
            {
                provider = "openai",
                baseUrl = upstream.BaseUrl,
                model = "gpt-4o",
                messages = new[] { new { role = "user", content = "Hi" } }
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(upstream.LastPath); // never reached the upstream
        }

        [Fact]
        public async Task Chat_Returns502_WhenUpstreamUnreachable()
        {
            var response = await _client.PostAsJsonAsync("/api/ai/chat", new
            {
                provider = "lmStudio",
                baseUrl = $"http://127.0.0.1:{FakeUpstream.GetFreePort()}", // nothing listening here
                model = "qwen/qwen3-8b",
                messages = new[] { new { role = "user", content = "Hi" } }
            });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains("Could not reach", (string)json["error"]!["message"]);
        }

        // ── /api/ai/models ───────────────────────────────────────────────────

        [Fact]
        public async Task Models_ListsOpenAiCompatibleCatalog()
        {
            await using var upstream = new FakeUpstream();

            var response = await _client.GetAsync($"/api/ai/models?provider=lmStudio&baseUrl={Uri.EscapeDataString(upstream.BaseUrl)}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            var models = ((JArray)json["models"]!).Select(m => (string)m!).ToList();
            Assert.Contains("qwen/qwen3-8b", models);
            Assert.EndsWith("/v1/models", upstream.LastPath);
        }

        [Fact]
        public async Task Models_ListsOllamaCatalog()
        {
            await using var upstream = new FakeUpstream();

            var response = await _client.GetAsync($"/api/ai/models?provider=ollama&baseUrl={Uri.EscapeDataString(upstream.BaseUrl)}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            var models = ((JArray)json["models"]!).Select(m => (string)m!).ToList();
            Assert.Contains("llama3.1:8b", models);
            Assert.EndsWith("/api/tags", upstream.LastPath);
        }

        [Fact]
        public async Task Models_Returns502_WhenUpstreamUnreachable()
        {
            var response = await _client.GetAsync($"/api/ai/models?provider=lmStudio&baseUrl={Uri.EscapeDataString($"http://127.0.0.1:{FakeUpstream.GetFreePort()}")}");

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains("Could not reach", (string)json["error"]!["message"]);
        }
    }
}
