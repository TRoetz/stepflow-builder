using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.Forms;

namespace StepFunctionsApp.Mcp
{
    // ═══════════════════════════════════════════════════════════════════
    // MCP FORM-CAPTURE TOOLS
    // Headless counterpart of the form-capture REST endpoints (and of wwwroot/form-capture.html):
    // inspect a suspended FormCapture task's bound form + attribute contract, then submit values.
    // Submissions are validated/coerced against the bound attribute domain and persisted as an EAV
    // row before the execution resumes — same pipeline as the REST surface (FormCaptureService).
    // ═══════════════════════════════════════════════════════════════════

    [McpServerToolType]
    public class FormCaptureTools
    {
        private readonly FormCaptureService _service;

        public FormCaptureTools(FormCaptureService service)
        {
            _service = service;
        }

        [McpServerTool]
        [Description(
"""
Get the form definition and bound attribute contract for a suspended form-capture task (a human task whose completion type is 'form'). Returns the task metadata, the full form definition (fields, layout) and the attributes of its bound domain — everything needed to build a valid submission. Use list_human_tasks to find tasks first.
""")]
        public async Task<string> GetFormCapture([Description("Task id (from list_human_tasks).")] string taskId)
        {
            var (status, view, errorBody) = await _service.GetAsync(taskId);
            if (view != null) return JsonConvert.SerializeObject(view, McpJson.Settings);

            // 404/500 bodies carry a single "error" message — surface it in the standard tool-error shape.
            var message = (string?)errorBody?["error"] ?? $"Form capture task '{taskId}' request failed (HTTP {status})";
            return Error(message);
        }

        [McpServerTool]
        [Description(
"""
Submit values for a suspended form-capture task: validates and coerces the values against the bound attribute domain, persists an EAV row (bound forms only), then resumes the execution. values is a JSON object mapping attribute names to values; call get_form_capture first to see the expected attributes and their types/constraints. The resumed run continues in the background — poll get_execution for the final status and output.
""")]
        public async Task<string> SubmitFormCapture(
            [Description("Task id (from list_human_tasks).")] string taskId,
            [Description("Values as a JSON object: {\"<attributeName>\": value}. Example: {\"amount\": 120.5, \"currency\": \"USD\"}")] string valuesJson)
        {
            JObject values;
            try
            {
                var parsed = JToken.Parse(valuesJson);
                if (parsed.Type != JTokenType.Object) return Error("values must be a JSON object mapping attribute names to values.");
                values = (JObject)parsed;
            }
            catch (System.Exception ex)
            {
                return Error($"values is not valid JSON: {ex.Message}");
            }

            var (status, body) = await _service.SubmitAsync(taskId, new JObject { ["values"] = values });
            if (status < 400) return JsonConvert.SerializeObject(body, McpJson.Settings);

            // Pass structured failures through unchanged ({error: ...} or {errors: {...}}).
            return JsonConvert.SerializeObject(body, McpJson.Settings);
        }

        private static string Error(string message) => JsonConvert.SerializeObject(new { error = message });
    }
}
