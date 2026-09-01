using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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

        public AiProxyController(IHttpClientFactory httpClientFactory, ILogger<AiProxyController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
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

            // Mirror the UI's stripV1Suffix: users may save the base URL with or without /v1.
            var baseUrl = StripV1(request.BaseUrl.Trim());
            var url = $"{baseUrl}/v1/chat/completions";

            var payload = new JObject
            {
                ["model"] = request.Model,
                ["messages"] = JArray.FromObject(request.Messages.Select(m => new JObject
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content
                }))
            };
            if (request.MaxTokens is int maxTokens) payload["max_tokens"] = maxTokens;
            if (request.Temperature is double temperature) payload["temperature"] = temperature;
            if (request.TopP is double topP) payload["top_p"] = topP;

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
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = new { message = $"Could not reach the AI server at {request.BaseUrl.Trim()}. Is it running and reachable from this machine? ({DescribeNetworkError(ex)})" }
                });
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("AI proxy: upstream {Url} returned HTTP {Status}: {Body}", url, (int)response.StatusCode, Truncate(body));
                    return StatusCode(StatusCodes.Status502BadGateway, new
                    {
                        error = new { message = $"Upstream AI server returned HTTP {(int)response.StatusCode}. {Truncate(ExtractErrorMessage(body))}" }
                    });
                }

                // Pass the upstream body through verbatim — the UI parses OpenAI-compatible responses.
                return Content(body, "application/json");
            }
        }

        // ── GET /api/ai/models?provider=...&baseUrl=... — list models installed on the endpoint ──

        [HttpGet("api/ai/models")]
        public async Task<IActionResult> ListModels([FromQuery] string provider, [FromQuery] string baseUrl, CancellationToken ct)
        {
            var validationError = ValidateLocalTarget(provider, baseUrl);
            if (validationError is not null) return BadRequest(new { error = new { message = validationError } });

            var root = StripV1(baseUrl.Trim());
            // Ollama exposes its catalog at /api/tags; every other local server speaks the OpenAI-compatible /v1/models.
            var url = provider.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? $"{root}/api/tags" : $"{root}/v1/models";

            var client = _httpClientFactory.CreateClient("ai-proxy");
            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                _logger.LogWarning(ex, "AI proxy: could not list models at {Url}", url);
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = new { message = $"Could not reach the AI server at {baseUrl.Trim()}. Is it running and reachable from this machine? ({DescribeNetworkError(ex)})" }
                });
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode(StatusCodes.Status502BadGateway, new
                    {
                        error = new { message = $"Upstream AI server returned HTTP {(int)response.StatusCode} while listing models. {Truncate(ExtractErrorMessage(body))}" }
                    });
                }

                var names = ParseModelNames(provider, body);
                return Ok(new { models = names });
            }
        }

        // ── Helpers ──

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
