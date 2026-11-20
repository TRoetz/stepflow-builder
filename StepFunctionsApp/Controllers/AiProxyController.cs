using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.Controllers
{
    // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    // AI PROXY CONTROLLER
    // Server-side forwarder for local LLM endpoints (LM Studio, Ollama, llama.cpp,
    // OpenAI-compatible). The browser cannot call a remote LAN endpoint directly —
    // LM Studio's server does not send CORS headers by default — so the UI routes
    // its local-provider traffic through these endpoints and we relay it.
    // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

    [ApiController]
    public class AiProxyController : ControllerBase
    {
        /// <summary>Providers this proxy serves. Cloud providers (OpenAI/Azure/Anthropic) send CORS headers themselves and keep calling directly from the browser.</summary>
        private static readonly HashSet<string> LocalProviders = new(StringComparer.OrdinalIgnoreCase)
        {
            "ollama", "lmStudio", "llamaCpp", "openaiCompatible"
        };

        private const int UpstreamErrorDetailLimit = 300;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AiProxyController> _logger;
        private readonly IConfiguration _config;

        public AiProxyController(IHttpClientFactory httpClientFactory, ILogger<AiProxyController> logger, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _config = config;
        }

        // ── Request models (camelCase on the wire via Newtonsoft binding) ──

        public sealed class AiChatRequest
        {
            public string Provider { get; set; } = "";
            public string BaseUrl { get; set; } = "";
            public string? ApiKey { get; set; }
            public string Model { get; set; } = "";
            public List<AiChatMessage> Messages { get; set; } = new();
            public int? MaxTokens { get; set; }
            public double? Temperature { get; set; }
            public double? TopP { get; set; }
        }

        public sealed class AiChatMessage
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
        }

        // ── POST /api/ai/chat — relay one chat completion to the configured local endpoint ──

        [HttpPost("api/ai/chat")]
        public async Task<IActionResult> Chat([FromBody] AiChatRequest request, CancellationToken ct)
        {
            var validationError = ValidateLocalTarget(request?.Provider, request?.BaseUrl);
            if (validationError is not null) return BadRequest(new { error = new { message = validationError } });
            if (string.IsNullOrWhiteSpace(request.Model))
                return BadRequest(new { error = new { message = "model is required." } });
            if (request.Messages is null || request.Messages.Count == 0
                || request.Messages.Any(m => string.IsNullOrWhiteSpace(m.Role) || m.Content is null))
                return BadRequest(new { error = new { message = "messages must be a non-empty list of { role, content } objects." } });

            var relay = await RelayChatCompletionAsync(request.Provider, request.BaseUrl, request.Model, request.Messages, request.MaxTokens, request.Temperature, request.TopP, ct);
            if (relay.Error is not null)
                return StatusCode(StatusCodes.Status502BadGateway, new { error = new { message = relay.Error } });

            // Pass the upstream body through verbatim — the UI parses OpenAI-compatible responses.
            return Content(relay.RawBody!, "application/json");
        }

        // ── POST /api/ai/ask — single-shot question for ai:// resource states (engine-facing) ──
        // The engine posts the state's input JToken verbatim and uses our response as the state output,
        // except when isError is true — then it fails the run with errorMessage. So every failure mode
        // returns 200 + { isError: true, errorMessage } rather than an HTTP error status.

        [HttpPost("api/ai/ask")]
        public async Task<IActionResult> Ask([FromBody] JToken? body, CancellationToken ct)
        {
            if (body is not JObject obj)
                return AskError("request must be a JSON object with at least one of: prompt, messages.");

            // Defaults come from the Ai: config section; per-request fields win when present.
            var provider = (string?)obj["provider"] ?? _config["Ai:Provider"];
            var baseUrl = (string?)obj["baseUrl"] ?? _config["Ai:BaseUrl"];
            var model = (string?)obj["model"] ?? _config["Ai:Model"];
            int? maxTokens = obj["maxTokens"] is { Type: JTokenType.Integer or JTokenType.Float } mt ? (int)mt : null;
            double? temperature = obj["temperature"] is { Type: JTokenType.Integer or JTokenType.Float } t ? (double)t : null;

            List<AiChatMessage> messages;
            var promptToken = obj["prompt"];
            if (promptToken != null && promptToken.Type != JTokenType.String)
                return AskError("prompt must be a string.");
            var prompt = (string?)promptToken;
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                messages = new List<AiChatMessage> { new() { Role = "user", Content = prompt! } };
            }
            else if (obj["messages"] is JArray arr)
            {
                try
                {
                    messages = arr.ToObject<List<AiChatMessage>>() ?? new();
                }
                catch (Exception ex)
                {
                    return AskError($"messages must be a list of {{ role, content }} objects: {ex.Message}");
                }
            }
            else
            {
                return AskError("either prompt or messages is required.");
            }

            var validationError = ValidateLocalTarget(provider, baseUrl);
            if (validationError is not null) return AskError(validationError);
            if (messages.Count == 0 || messages.Any(m => string.IsNullOrWhiteSpace(m.Role) || m.Content is null))
                return AskError("messages must be a non-empty list of { role, content } objects.");

            // Model auto-discovery: first id from the endpoint's catalog when no model was pinned.
            if (string.IsNullOrWhiteSpace(model))
            {
                var (ok, names, error) = await FetchModelNamesAsync(provider!, baseUrl!, ct);
                if (!ok || names.Count == 0) return AskError(error ?? $"No models found at {baseUrl}.");
                model = names[0];
            }

            var relay = await RelayChatCompletionAsync(provider!, baseUrl!, model, messages, maxTokens, temperature, null, ct);
            if (relay.Error is not null) return AskError(relay.Error);
            if (string.IsNullOrEmpty(relay.Answer))
                return AskError("Upstream returned HTTP 200 but the completion body has no choices[0].message.content.");

            return Ok(new { answer = relay.Answer });
        }

        /// <summary>Engine contract: failures are data, not status codes — the invoker checks isError, not HTTP.</summary>
        private IActionResult AskError(string message) => Ok(new { isError = true, errorMessage = message }); // engine contract: it checks isError, not HTTP status

        // ── GET /api/ai/models?provider=...&baseUrl=... — list models installed on the endpoint ──

        [HttpGet("api/ai/models")]
        public async Task<IActionResult> ListModels([FromQuery] string provider, [FromQuery] string baseUrl, CancellationToken ct)
        {
            var validationError = ValidateLocalTarget(provider, baseUrl);
            if (validationError is not null) return BadRequest(new { error = new { message = validationError } });

            var (ok, names, error) = await FetchModelNamesAsync(provider, baseUrl, ct);
            if (!ok)
                return StatusCode(StatusCodes.Status502BadGateway, new { error = new { message = error! } });

            return Ok(new { models = names });
        }

        // ── Helpers ──

        /// <summary>Outcome of one relayed chat completion: either the upstream 2xx body (+ parsed answer) or a human-readable error.</summary>
        private sealed class RelayResult
        {
            public string? RawBody;   // verbatim upstream 2xx body (chat passthrough)
            public string? Answer;    // choices[0].message.content, when present in the body
            public string? Error;     // set on any failure — network or non-2xx upstream
        }

        /// <summary>Shared relay core: build the OpenAI-compatible payload and POST it to {baseUrl}/v1/chat/completions via the "ai-proxy" client.</summary>
        private async Task<RelayResult> RelayChatCompletionAsync(
            string provider, string baseUrl, string model, List<AiChatMessage> messages,
            int? maxTokens, double? temperature, double? topP, CancellationToken ct)
        {
            var result = new RelayResult();

            // Mirror the UI's stripV1Suffix: users may save the base URL with or without /v1.
            var url = $"{StripV1(baseUrl.Trim())}/v1/chat/completions";

            var payload = new JObject
            {
                ["model"] = model,
                ["messages"] = JArray.FromObject(messages.Select(m => new JObject
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content
                }))
            };
            if (maxTokens is int max) payload["max_tokens"] = max;
            if (temperature is double temp) payload["temperature"] = temp;
            if (topP is double top) payload["top_p"] = top;

            var client = _httpClientFactory.CreateClient("ai-proxy");
            using var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(url, content, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                _logger.LogWarning(ex, "AI proxy: could not reach local LLM endpoint at {Url}", url);
                result.Error = $"Could not reach the AI server at {baseUrl.Trim()}. Is it running and reachable from this machine? ({DescribeNetworkError(ex)})";
                return result;
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("AI proxy: upstream {Url} returned HTTP {Status}: {Body}", url, (int)response.StatusCode, Truncate(body));
                    result.Error = $"Upstream AI server returned HTTP {(int)response.StatusCode}. {Truncate(ExtractErrorMessage(body))}";
                    return result;
                }

                result.RawBody = body;
                try
                {
                    var parsed = JObject.Parse(body);
                    result.Answer = (string?)parsed?["choices"]?[0]?["message"]?["content"];
                }
                catch (JsonException)
                {
                    // Non-JSON success body — RawBody is still passed through for /api/ai/chat.
                }
            }
            return result;
        }

        /// <summary>Shared model catalog fetch: Ollama at /api/tags, every other local server at the OpenAI-compatible /v1/models.</summary>
        private async Task<(bool Ok, List<string> Names, string? Error)> FetchModelNamesAsync(string provider, string baseUrl, CancellationToken ct)
        {
            var url = provider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
                ? $"{StripV1(baseUrl.Trim())}/api/tags"
                : $"{StripV1(baseUrl.Trim())}/v1/models";

            var client = _httpClientFactory.CreateClient("ai-proxy");
            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                _logger.LogWarning(ex, "AI proxy: could not list models at {Url}", url);
                return (false, new List<string>(), $"Could not reach the AI server at {baseUrl.Trim()}. Is it running and reachable from this machine? ({DescribeNetworkError(ex)})");
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    return (false, new List<string>(), $"Upstream AI server returned HTTP {(int)response.StatusCode} while listing models. {Truncate(ExtractErrorMessage(body))}");

                return (true, ParseModelNames(provider, body), null);
            }
        }

        /// <summary>Shared guard: only local providers may be proxied, and the target must be an http(s) URL.</summary>
        private static string? ValidateLocalTarget(string? provider, string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(provider)) return "provider is required.";
            if (!LocalProviders.Contains(provider))
                return $"Provider '{provider}' is not a local endpoint. This proxy only serves ollama, lmStudio, llamaCpp and openaiCompatible.";
            if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return "baseUrl must be an absolute http(s) URL (e.g. http://192.168.10.34:1234).";
            return null;
        }

        /// <summary>Strip a trailing /v1 or /v1/ so callers can append their own path segment — same rule as the UI's stripV1Suffix.</summary>
        private static string StripV1(string url)
        {
            var trimmed = url.TrimEnd('/');
            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^3];
            return trimmed;
        }

        /// <summary>Pull a human-readable message out of an OpenAI-style error body, falling back to the raw text.</summary>
        private static string ExtractErrorMessage(string body)
        {
            try
            {
                var parsed = JObject.Parse(body);
                var message = (string?)parsed?["error"]?["message"] ?? (string?)parsed?["error"];
                if (!string.IsNullOrWhiteSpace(message)) return message;
            }
            catch (JsonException)
            {
                // Not JSON — fall through to raw text.
            }
            return body.Trim();
        }

        private static string Truncate(string value)
        {
            var trimmed = value?.Trim() ?? "";
            return trimmed.Length <= UpstreamErrorDetailLimit ? trimmed : trimmed[..UpstreamErrorDetailLimit] + "…";
        }

        private static string DescribeNetworkError(Exception ex) =>
            ex is TaskCanceledException && !(ex.InnerException is HttpRequestException)
                ? "request timed out"
                : (ex.Message ?? ex.GetType().Name);

        /// <summary>Normalize the two catalog shapes: Ollama { models: [{ name }] } and OpenAI-compatible { data: [{ id }] }.</summary>
        private static List<string> ParseModelNames(string provider, string body)
        {
            try
            {
                var parsed = JObject.Parse(body);
                if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                    return (parsed?["models"] as JArray ?? new JArray())
                        .Select(m => (string?)m?["name"])
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList();
                return (parsed?["data"] as JArray ?? new JArray())
                    .Select(m => (string?)m?["id"])
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToList();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }
    }
}
