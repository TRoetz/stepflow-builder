using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // AI DECISION SERVICE
    // Now calls the remote LLM UI app via HTTP.
    // ═══════════════════════════════════════════════════════════════════════════════

    public class AiDecisionService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AiDecisionService> _logger;
        private readonly string _llmBaseUrl = "http://localhost:5000"; // Should be in config

        private const string DefaultSystemPrompt = """
            You are a decision engine inside an automated compliance workflow.
            You will receive a question and optional context data.
            
            You MUST respond with ONLY a JSON object in this exact format — no markdown, no backticks, no extra text:
            {
              "answer": "YES" or "NO",
              "confidence": 0.0 to 1.0,
              "reasoning": "Brief explanation of why"
            }
            
            Rules:
            - answer MUST be exactly "YES" or "NO" (uppercase)
            - confidence MUST be a number between 0.0 and 1.0
            - reasoning should be concise (1-2 sentences)
            - If you are unsure, lean towards "NO" (conservative)
            - Base your decision strictly on the provided context data
            """;

        public AiDecisionService(
            IHttpClientFactory httpClientFactory,
            ILogger<AiDecisionService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<AiDecisionResult> AskAsync(AiDecisionInput request, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var systemPrompt = request.SystemPrompt ?? DefaultSystemPrompt;
                var userText = BuildUserMessage(request);

                // Call the Remote LLM via OpenAI-compatible endpoint or internal chat API
                var payload = new
                {
                    model = request.Provider, // Mapped to provider name
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userText }
                    }
                };

                var client = _httpClientFactory.CreateClient();
                var response = await client.PostAsync($"{_llmBaseUrl}/v1/chat/completions",
                    new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"), ct);

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(ct);
                var openAiResponse = JObject.Parse(content);
                var aiText = openAiResponse["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";

                sw.Stop();
                var result = ParseAiResponse(aiText, request.Provider ?? "Default", "Default", sw.Elapsed.TotalMilliseconds);

                if (request.ConfidenceThreshold > 0 && result.Confidence < request.ConfidenceThreshold)
                {
                    result.Decision = false;
                    result.MeetsThreshold = false;
                }

                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "AI decision call failed");
                return new AiDecisionResult
                {
                    Decision = false,
                    Answer = "NO",
                    Reasoning = $"AI call failed: {ex.Message}",
                    Provider = request.Provider ?? "Default",
                    DurationMs = sw.Elapsed.TotalMilliseconds,
                    IsError = true,
                    ErrorMessage = ex.Message
                };
            }
        }

        private string BuildUserMessage(AiDecisionInput request)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"**Question:** {request.Question}");

            if (request.Context != null)
            {
                var contextStr = request.Context is string s
                    ? s
                    : JsonConvert.SerializeObject(request.Context, Formatting.Indented);
                sb.AppendLine();
                sb.AppendLine("**Context Data:**");
                sb.AppendLine(contextStr);
            }

            sb.AppendLine();
            sb.AppendLine("Respond with ONLY the JSON object. No markdown, no backticks.");
            return sb.ToString();
        }

        private AiDecisionResult ParseAiResponse(string rawResponse, string providerName, string model, double durationMs)
        {
            var result = new AiDecisionResult
            {
                Provider = providerName,
                Model = model,
                DurationMs = durationMs
            };

            try
            {
                var jsonStr = ExtractJson(rawResponse);
                var parsed = JObject.Parse(jsonStr);

                var answer = parsed["answer"]?.ToString()?.Trim().ToUpperInvariant() ?? "";
                result.Answer = answer;
                result.Decision = answer == "YES";
                result.Confidence = parsed["confidence"]?.Value<double>() ?? 0.5;
                result.Reasoning = parsed["reasoning"]?.ToString() ?? "";
                result.MeetsThreshold = true;
            }
            catch
            {
                var upper = rawResponse.ToUpperInvariant();
                result.Decision = Regex.IsMatch(upper, @"\bYES\b");
                result.Answer = result.Decision ? "YES" : "NO";
                result.Confidence = 0.5;
                result.Reasoning = rawResponse.Length > 200 ? rawResponse[..200] + "..." : rawResponse;
            }

            return result;
        }

        private static string ExtractJson(string text)
        {
            text = Regex.Replace(text, @"```json\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"```\s*", "");
            text = text.Trim();
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start) return text[start..(end + 1)];
            return text;
        }
    }
}
