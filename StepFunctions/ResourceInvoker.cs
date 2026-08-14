using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Web;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // RESOURCE INVOKER & HANDLERS
    // ═══════════════════════════════════════════════════════════════════════════════

    public interface IResourceInvoker
    {
        Task<JToken> InvokeAsync(string resource, JToken input, CancellationToken ct);
    }

    public class CompositeResourceInvoker : IResourceInvoker
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly RuleEngineService _ruleEngine;
        private readonly MicrosoftRulesEngineService _msRulesEngine;
        private readonly DuckDbTransformService _duckDbTransform;
        private readonly EavRegistryService _eavRegistry;
        private readonly ScriptExecutionService _scriptExecution;
        private readonly Lazy<StepFunctionService> _stepService;
        private readonly ILogger<CompositeResourceInvoker> _logger;
        private readonly string _callbackBaseUrl = "http://localhost:5000"; // Should come from config

        public CompositeResourceInvoker(
            IHttpClientFactory httpClientFactory,
            RuleEngineService ruleEngine,
            MicrosoftRulesEngineService msRulesEngine,
            DuckDbTransformService duckDbTransform,
            EavRegistryService eavRegistry,
            ScriptExecutionService scriptExecution,
            Lazy<StepFunctionService> stepService,
            ILogger<CompositeResourceInvoker> logger)
        {
            _httpClientFactory = httpClientFactory;
            _ruleEngine = ruleEngine;
            _msRulesEngine = msRulesEngine;
            _duckDbTransform = duckDbTransform;
            _eavRegistry = eavRegistry;
            _scriptExecution = scriptExecution;
            _stepService = stepService;
            _logger = logger;
        }

        public async Task<JToken> InvokeAsync(string resource, JToken input, CancellationToken ct)
        {
            if (resource.StartsWith("http://") || resource.StartsWith("https://"))
                return await InvokeHttpAsync(resource, input, ct);

            if (resource.StartsWith("rule://"))
                return await HandleRuleAsync(resource, input, ct);

            // Microsoft RulesEngine: rules://<workflowName>
            if (resource.StartsWith("rules://"))
                return await HandleMsRulesAsync(resource, input, ct);

            // DuckDB Transform: transform://<operation>
            if (resource.StartsWith("transform://"))
                return await HandleTransformAsync(resource, input, ct);

            if (resource.StartsWith("ai://"))
                return await HandleAiAsync(resource, input, ct);

            if (resource.StartsWith("flow://"))
                return await HandleFlowAsync(resource, input, ct);

            if (resource.StartsWith("tool://"))
                return await HandleToolAsync(resource, input, ct);

            if (resource.StartsWith("internal://"))
                return await HandleInternalAsync(resource, input, ct);

            throw new StepEngineException("States.TaskFailed", $"Unknown resource scheme: {resource}");
        }

        private async Task<JToken> HandleToolAsync(string resource, JToken input, CancellationToken ct)
        {
            var toolName = resource["tool://".Length..].Trim('/');
            var url = $"{_callbackBaseUrl}/api/tools/{toolName}/execute";
            return await InvokeHttpAsync(url, input, ct);
        }

        private async Task<JToken> HandleMsRulesAsync(string resource, JToken input, CancellationToken ct)
        {
            var workflowName = resource["rules://".Length..].Trim('/');
            _logger.LogInformation("Executing Microsoft RulesEngine workflow: {Workflow}", workflowName);

            var inputObj = input as JObject ?? new JObject();
            var result = _msRulesEngine.ExecuteWorkflow(workflowName, inputObj);
            return JObject.FromObject(result);
        }

        private async Task<JToken> HandleTransformAsync(string resource, JToken input, CancellationToken ct)
        {
            var operation = resource["transform://".Length..].Trim('/');
            var lowerOp = operation.ToLowerInvariant();

            if (lowerOp == "javascript" || lowerOp == "python" || lowerOp == "powershell" || lowerOp == "csharp" || lowerOp == "shell")
            {
                _logger.LogInformation("Routing scripting execution to ScriptExecutionService. Language: {Language}", operation);
                
                var script = input["script"]?.ToString() ?? input["parameters"]?["script"]?.ToString() ?? "";
                var inputData = input["input_data"] ?? input["parameters"]?["input_data"] ?? input;
                
                return await _scriptExecution.ExecuteScriptAsync(operation, script, inputData, ct);
            }

            _logger.LogDebug("Executing DuckDB transform: {Operation}", operation);

            var inputObj = input as JObject ?? new JObject();
            // Override operation from resource URI if provided
            if (!string.IsNullOrEmpty(operation) && operation != "query")
                inputObj["operation"] = operation;

            var result = _duckDbTransform.ExecuteTransform(inputObj);
            return result;
        }

        private async Task<JToken> InvokeHttpAsync(string url, JToken input, CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient();
            HttpRequestMessage request;

            // Detect structured handler from ASL Parameters mapping
            if (input is JObject obj && obj["__handler"]?.ToString() == "http")
            {
                var methodStr = obj["method"]?.ToString() ?? "POST";
                var method = new HttpMethod(methodStr.ToUpper());
                var body = obj["body"] ?? new JObject();
                var query = obj["query"] as JObject;
                var auth = obj["auth"] as JObject;

                // 1. Construct URL with Query Parameters
                if (query != null && query.HasValues)
                {
                    var uriBuilder = new UriBuilder(url);
                    var q = HttpUtility.ParseQueryString(uriBuilder.Query);
                    foreach (var prop in query.Properties())
                    {
                        q[prop.Name] = prop.Value.ToString();
                    }
                    uriBuilder.Query = q.ToString();
                    url = uriBuilder.ToString();
                }

                request = new HttpRequestMessage(method, url);

                // 2. Add Authentication Headers
                if (auth != null)
                {
                    var authType = auth["type"]?.ToString();
                    var token = auth["token"]?.ToString();
                    if (!string.IsNullOrEmpty(token) && (authType == "Bearer" || authType == "OAuth"))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    }
                }

                // 3. Add JSON Body (except for GET/DELETE)
                if (method != HttpMethod.Get && method != HttpMethod.Delete)
                {
                    request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                }
            }
            else
            {
                // Fallback to legacy behavior: simple POST with the entire input as body
                request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(input.ToString(Formatting.None), Encoding.UTF8, "application/json")
                };
            }

            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(ct);
            try { return JToken.Parse(content); } catch { return new JObject { ["body"] = content }; }
        }

        private async Task<JToken> HandleRuleAsync(string resource, JToken input, CancellationToken ct)
        {
            var uri = new Uri(resource);
            var ruleId = uri.Host; // e.g., rule://CheckCredit -> ruleId is CheckCredit

            // Extract EAV query parameter
            var query = HttpUtility.ParseQueryString(uri.Query);
            var eavEntityName = query["eav"];

            Dictionary<string, object> parameters;

            if (!string.IsNullOrEmpty(eavEntityName))
            {
                // 1. Map dynamic JSON to strict EAV dictionary
                parameters = _eavRegistry.MapPayloadToEav(eavEntityName, input);
            }
            else
            {
                // Fallback to legacy naive object mapping
                parameters = input.ToObject<Dictionary<string, object>>() ?? new();
            }

            // 2. Execute the rule safely
            var result = _ruleEngine.ExecuteRuleById(ruleId, parameters);
            return JObject.FromObject(result);
        }

        private async Task<JToken> HandleAiAsync(string resource, JToken input, CancellationToken ct)
        {
            var url = $"{_callbackBaseUrl}/api/ai/ask";
            var result = await InvokeHttpAsync(url, input, ct);
            
            var isError = result["isError"]?.Value<bool>() ?? false;
            if (isError) throw new StepEngineException("States.TaskFailed", $"AI failed: {result["errorMessage"]}");
            
            return result;
        }

        private async Task<JToken> HandleFlowAsync(string resource, JToken input, CancellationToken ct)
        {
            var flowId = resource["flow://".Length..].Trim('/');
            var execution = await _stepService.Value.ExecuteSyncAsync(flowId, input, ct);
            if (execution.Status == ExecutionStatus.Failed) 
                throw new StepEngineException(execution.ErrorCode ?? "SubFlow.Failed", execution.ErrorMessage ?? "Sub-flow failed");
            return execution.Output;
        }

        private Task<JToken> HandleInternalAsync(string resource, JToken input, CancellationToken ct)
        {
            var path = resource["internal://".Length..];
            return Task.FromResult<JToken>(path switch
            {
                "echo" => input.DeepClone(),
                "engine/status" => JToken.FromObject(_ruleEngine.GetStatus()),
                "rules/status" => JToken.FromObject(_msRulesEngine.GetStatus()),
                "transform/status" => JToken.FromObject(_duckDbTransform.GetStatus()),
                _ => throw new StepEngineException("States.TaskFailed", $"Unknown internal resource: {resource}")
            });
        }
    }
}
