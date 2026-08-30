using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Mcp
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // MCP FLOW TOOLS
    // Exposes the .NET flow engine to AI harnesses over Model Context Protocol.
    // Definitions use Amazon States Language (camelCase JSON) — the same format the
    // React UI exports/imports, so flows created here load into the canvas unchanged.
    // ═══════════════════════════════════════════════════════════════════════════════

    [McpServerToolType]
    public class FlowTools
    {
        private readonly StepFunctionService _service;

        // camelCase + no nulls: definitions round-trip with the React UI (importFlow)
        // and stay compact for LLM context windows. Dictionary keys (state names)
        // keep their original casing — the stock camelCase resolver would lowercase them.
        private static readonly JsonSerializerSettings Json = new()
        {
            ContractResolver = new KeyPreservingCamelCaseResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        public FlowTools(StepFunctionService service) => _service = service;

        [McpServerTool]
        [Description("List all flows registered on the .NET Step Functions engine. Returns id, name, description and updatedAt for each flow.")]
        public string ListFlows()
        {
            var flows = _service.ListStateMachines().Select(sm => new
            {
                sm.Id,
                sm.Name,
                sm.Description,
                UpdatedAt = sm.UpdatedAt.ToString("o")
            });
            return JsonConvert.SerializeObject(flows, Json);
        }

        [McpServerTool]
        [Description("Get a flow's metadata and full Amazon States Language definition as camelCase JSON (compatible with the React UI import). Accepts the flow id or its name.")]
        public string GetFlow([Description("Flow id or name")] string idOrName)
        {
            var sm = _service.GetStateMachine(idOrName);
            if (sm == null) return Error($"Flow '{idOrName}' not found. Use list_flows to see registered flows.");

            var definition = JToken.Parse(JsonConvert.SerializeObject(sm.Definition, Json));
            return JsonConvert.SerializeObject(new
            {
                sm.Id,
                sm.Name,
                sm.Description,
                CreatedAt = sm.CreatedAt.ToString("o"),
                UpdatedAt = sm.UpdatedAt.ToString("o"),
                Definition = definition
            }, Json);
        }

        [McpServerTool]
        [Description(
"""
Create a new flow or replace an existing one with the same name (upsert). Returns the stored id and updatedAt.

Definition format (Amazon States Language, camelCase JSON):
  startAt: name of the first state.
  states: object mapping state name -> state definition. Every non-terminal state must set "next" to the next state name; terminal states omit it (or set "end": true).

State types supported by this .NET engine:
  - Task:     { "type": "Task", "resource": "<uri>", "parameters": {...}, "resultPath"? } — executes a resource.
  - Pass:     { "type": "Pass", "result"? } — passes input through (optionally replaced by result).
  - Choice:   { "type": "Choice", "choices": [ { "variable": "$.field", "<operator>": value, "next": "<state>" }, ... ], "default"? }
              Operators: stringEquals, stringEqualsPath, stringGreaterThan, stringLessThan, numericEquals, numericGreaterThan, numericGreaterThanEquals, numericLessThan, numericLessThanEquals, booleanEquals, timestampEquals, timestampGreaterThan, timestampLessThan, isPresent, isNull, isString, isNumeric, isBoolean, stringMatches, and/or/not (nested).
  - Wait:     { "type": "Wait", "seconds": N } or { "type": "Wait", "timestampPath": "$.ts" }.
  - Parallel: { "type": "Parallel", "branches": [ { "startAt": "...", "states": {...} }, ... ] }.
  - Map:      { "type": "Map", "itemsPath": "$.items", "iterator": { "startAt": "...", "states": {...} }, "maxConcurrency"? }.
  - Succeed:  { "type": "Succeed" } — ends the flow successfully.
  - Fail:     { "type": "Fail", "error": "...", "cause"? }.

Task state resource schemes handled by this engine:
  - http:// or https://<url> : HTTP request; the state input is sent as the JSON body.
  - transform://query        : DuckDB SQL query; parameters { "sql": "SELECT ..." }.
  - transform://filter|project|aggregate|sort|lookup|schema|sample|execute : DuckDB table operations (parameters: table, where, columns, groupBy, orderBy, sourceTable, lookupTable, limit).
  - transform://javascript|python|powershell|csharp|shell : run a script; parameters { "script": "...", "input_data"? }.
  - rule://<RuleId>[?eav=<entity>] : execute a stored SQL rule by id with the state input as parameters.
  - rules://<workflowName>   : Microsoft RulesEngine workflow.
  - ai://<service>           : AI decision via the React app's /api/ai/ask (requires the UI backend on port 5000).
  - flow://<flowIdOrName>    : run another registered flow as a sub-flow.
  - internal://echo          : returns the input unchanged (useful for testing); also engine/status, rules/status, transform/status.

Parameter values may reference the state input with JSONPath ("$.field") and use States.* intrinsics such as States.Format("...").
Note: "sql://" is a React-UI-only scheme and is NOT handled by this .NET engine — use transform://query for SQL here.

Example statesJson:
{"Start":{"type":"Pass","next":"Echo"},"Echo":{"type":"Task","resource":"internal://echo","parameters":{"note":"hello"}},"Done":{"type":"Succeed"}}
""")]
        public string SaveFlow(
            [Description("Unique flow name; saving again with the same name replaces that flow's definition.")] string name,
            [Description("Amazon States Language states object as a JSON string: { \"<stateName>\": { \"type\": \"Task\", ... }, ... }")] string statesJson,
            [Description("Name of the first state. Defaults to the first key in states when omitted.")] string? startAt = null,
            [Description("Optional human-readable description.")] string? description = null)
        {
            JObject statesObj;
            try
            {
                statesObj = JObject.Parse(statesJson);
            }
            catch (Exception ex)
            {
                return Error($"states is not valid JSON: {ex.Message}");
            }

            if (statesObj.Count == 0) return Error("states must contain at least one state.");

            var effectiveStart = string.IsNullOrWhiteSpace(startAt) ? statesObj.Properties().First().Name : startAt;
            if (!statesObj.Properties().Any(p => p.Name == effectiveStart))
                return Error($"startAt '{effectiveStart}' is not a state. Available states: {string.Join(", ", statesObj.Properties().Select(p => p.Name))}");

            StateMachineDefinition def;
            try
            {
                def = new StateMachineDefinition
                {
                    StartAt = effectiveStart,
                    States = statesObj.ToObject<Dictionary<string, StateDefinition>>()!
                };
            }
            catch (Exception ex)
            {
                return Error($"Could not parse a state definition: {ex.Message}");
            }

            var sm = _service.RegisterStateMachine(name, def, description);
            return JsonConvert.SerializeObject(new
            {
                sm.Id,
                name = sm.Name,
                UpdatedAt = sm.UpdatedAt.ToString("o")
            }, Json);
        }

        [McpServerTool]
        [Description(
"""
Run a flow synchronously on the .NET engine and return its status, final output, per-state history and any error.
Accepts the flow id or name. input is an optional JSON object string (defaults to {}).
""")]
        public async Task<string> RunFlow(
            [Description("Flow id or name")] string idOrName,
            [Description("Optional execution input as a JSON object string, e.g. {\"orderId\": 42}. Defaults to {} when omitted.")] string? inputJson = null)
        {
            var sm = _service.GetStateMachine(idOrName);
            if (sm == null) return Error($"Flow '{idOrName}' not found. Use list_flows to see registered flows.");

            JToken input;
            try
            {
                input = string.IsNullOrWhiteSpace(inputJson) ? new JObject() : JToken.Parse(inputJson);
            }
            catch (Exception ex)
            {
                return Error($"input is not valid JSON: {ex.Message}");
            }

            var exec = await _service.ExecuteSyncAsync(sm.Id, input);
            return JsonConvert.SerializeObject(new
            {
                executionId = exec.ExecutionId,
                status = exec.Status.ToString(),
                output = exec.Output,
                errorCode = exec.ErrorCode,
                errorMessage = exec.ErrorMessage,
                history = exec.History.Select(h => new { type = h.Type, state = h.State, data = h.Data })
            }, Json);
        }

        private static string Error(string message) => JsonConvert.SerializeObject(new { error = message });

        /// <summary>
        /// camelCase property names, but leaves dictionary keys (state names) untouched.
        /// </summary>
        private sealed class KeyPreservingCamelCaseResolver : CamelCasePropertyNamesContractResolver
        {
            protected override string ResolveDictionaryKey(string key) => key;
        }
    }
}
