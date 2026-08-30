using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // FORM CAPTURE — runtime endpoints for suspended FormCapture states.
    // GET  api/form-captures/{taskId}          → form definition + bound attribute contract
    // POST api/form-captures/{taskId}/submit   → validate/coerce values, persist EAV row, resume
    // The standalone page wwwroot/form-capture.html is the only intended client; possession of
    // the taskId is the authorization (no auth exists anywhere in this app).
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    public class FormCaptureController : ControllerBase
    {
        private readonly IHumanTaskStore _taskStore;
        private readonly IFormDefinitionStore _formStore;
        private readonly IAttributeDomainStore _domainStore;
        private readonly EavRowStore _eavRows;
        private readonly StepFunctionService _stepService;

        public FormCaptureController(
            IHumanTaskStore taskStore,
            IFormDefinitionStore formStore,
            IAttributeDomainStore domainStore,
            EavRowStore eavRows,
            StepFunctionService stepService)
        {
            _taskStore = taskStore;
            _formStore = formStore;
            _domainStore = domainStore;
            _eavRows = eavRows;
            _stepService = stepService;
        }

        private static bool IsFormCapture(HumanTaskRecord record) =>
            string.Equals(record.CompletionType, "form", StringComparison.OrdinalIgnoreCase);

        private async Task<(HumanTaskRecord? record, FormDefinition? form)> LoadFormTaskAsync(string taskId)
        {
            var record = await _taskStore.LoadAsync(taskId);
            if (record == null || !IsFormCapture(record)) return (null, null);

            var formId = record.Payload?["task"]?["formId"]?.ToString();
            FormDefinition? form;
            // The version resolved at suspension time is authoritative for this task: a later
            // republish of the form must not change what a pending fill-in page renders/validates.
            if (string.IsNullOrEmpty(formId)) form = null;
            else if (!string.IsNullOrEmpty(record.FormVersion)) form = _formStore.Get(formId, record.FormVersion);
            else form = _formStore.Get(formId); // legacy records predate versioning → current version
            return (record, form);
        }

        // Form definition + bound attribute contract for the standalone page.
        [HttpGet("api/form-captures/{taskId}")]
        public async Task<IActionResult> Get(string taskId)
        {
            var (record, form) = await LoadFormTaskAsync(taskId);
            if (record == null) return NotFound(new { error = $"Form capture task '{taskId}' not found" });
            if (form == null) return StatusCode(500, new { error = "The form bound to this task no longer exists in the definition store" });

            var attributes = new List<EntityAttribute>();
            if (!string.IsNullOrEmpty(form.AttributeDomainName))
            {
                var (domain, _) = _domainStore.GetByName(form.AttributeDomainName);
                if (domain != null) attributes.AddRange(domain.Attributes);
            }

            return Ok(new
            {
                taskId = record.TaskId,
                status = record.Status.ToString(),
                title = record.Title ?? form.Title,
                assignee = record.Assignee,
                executionId = record.ExecutionId,
                stateMachineName = record.StateMachineName,
                createdAtUtc = record.CreatedAtUtc,
                completedAtUtc = record.CompletedAtUtc,
                result = record.Result,
                formUrl = $"/form-capture.html?taskId={record.TaskId}",
                form,
                attributes
            });
        }

        // Validate + coerce the submission against the bound domain's attribute contract,
        // persist an EAV row (bound forms only), then resume the suspended execution.
        [HttpPost("api/form-captures/{taskId}/submit")]
        public async Task<IActionResult> Submit(string taskId, [FromBody] JObject? body)
        {
            var (record, form) = await LoadFormTaskAsync(taskId);
            if (record == null) return NotFound(new { error = $"Form capture task '{taskId}' not found" });
            if (form == null) return StatusCode(500, new { error = "The form bound to this task no longer exists in the definition store" });
            if (record.Status != HumanTaskStatus.Pending)
                return Conflict(new { error = $"Form capture task '{taskId}' is already completed" });

            var valuesToken = body?["values"];
            if (valuesToken == null || valuesToken.Type != JTokenType.Object)
                return BadRequest(new { errors = new Dictionary<string, string> { ["values"] = "Body must be an object with a 'values' JSON object" } });

            EntityAttribute[]? contract = null;
            if (!string.IsNullOrEmpty(form.AttributeDomainName))
            {
                var (domain, _) = _domainStore.GetByName(form.AttributeDomainName);
                contract = domain?.Attributes.Where(a => !string.IsNullOrWhiteSpace(a.AttributeName)).ToArray();
            }

            var coerced = new JObject();
            var errors = new Dictionary<string, string>();
            if (contract == null)
            {
                // Unbound form: accept any JSON object as-is.
                foreach (var prop in ((JObject)valuesToken).Properties())
                    coerced[prop.Name] = prop.Value;
            }
            else
            {
                foreach (var attr in contract)
                {
                    var raw = valuesToken[attr.AttributeName];
                    if (!TryCoerceAttribute(attr, raw, out var value, out var error))
                        errors[attr.AttributeName] = error;
                    else if (value != null)
                        coerced[attr.AttributeName] = value;
                }
            }

            if (errors.Count > 0) return BadRequest(new { errors });

            // Persist the captured row before resuming so a resume failure never loses data.
            if (!string.IsNullOrEmpty(form.AttributeDomainName))
            {
                _eavRows.AppendRow(form.AttributeDomainName, new EavRow
                {
                    SourceTaskId = record.TaskId,
                    Values = coerced
                });
            }

            try
            {
                var completed = await _stepService.CompleteHumanTaskAsync(taskId, coerced);
                if (completed == null) return NotFound(new { error = $"Form capture task '{taskId}' not found" });
                return Ok(new { status = "completed", executionId = record.ExecutionId });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already"))
            {
                // Lost a race with another submit that completed the task first.
                return Conflict(new { error = $"Form capture task '{taskId}' is already completed" });
            }
        }

        /// <summary>
        /// Coerces one submitted value to its AttributeDataType and enforces the ValidationSchemaJson
        /// constraints (required, minimum/maximum, minLength/maxLength, pattern). Absent optional values
        /// coerce to null (skipped in the result object).
        /// </summary>
        private static bool TryCoerceAttribute(EntityAttribute attr, JToken? raw, out JToken? coercedValue, out string error)
        {
            coercedValue = null;
            error = "";

            var validation = ParseValidation(attr.ValidationSchemaJson);
            var required = attr.PrimaryKey || (validation?["required"]?.Type == JTokenType.Boolean && validation!["required"]!.Value<bool>());

            if (raw == null || raw.Type == JTokenType.Null)
            {
                if (required) error = "Required attribute";
                return error.Length == 0;
            }

            // Newtonsoft's DateParseHandling.Auto turns ISO-8601-looking strings into JTokenType.Date;
            // restore canonical round-trip text so every branch below sees stable, culture-free input.
            if (raw.Type == JTokenType.Date) raw = new JValue(raw.Value<DateTime>().ToString("o"));

            switch (attr.DataType)
            {
                case AttributeDataType.String:
                {
                    var s = raw.Type == JTokenType.String ? raw.Value<string>()! : raw.ToString(Formatting.None);
                    if (!CheckStringConstraints(s, validation, out error)) return false;
                    coercedValue = new JValue(s);
                    break;
                }
                case AttributeDataType.Number:
                {
                    double? number = null;
                    if (raw.Type == JTokenType.Integer || raw.Type == JTokenType.Float) number = raw.Value<double>();
                    else if (raw.Type == JTokenType.String && double.TryParse(raw.Value<string>(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        number = parsed;
                    if (number == null) { error = "Must be a number"; return false; }
                    if (!CheckNumberConstraints(number.Value, validation, out error)) return false;
                    coercedValue = new JValue(number.Value);
                    break;
                }
                case AttributeDataType.Boolean:
                {
                    bool? flag = null;
                    if (raw.Type == JTokenType.Boolean) flag = raw.Value<bool>();
                    else if (raw.Type == JTokenType.String && bool.TryParse(raw.Value<string>(), out var b)) flag = b;
                    if (flag == null) { error = "Must be a boolean"; return false; }
                    coercedValue = new JValue(flag.Value);
                    break;
                }
                case AttributeDataType.Date:
                {
                    string? s = raw.Type == JTokenType.String ? raw.Value<string>() : raw.ToString(Formatting.None);
                    if (s == null || !DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    { error = "Must be an ISO 8601 date"; return false; }
                    coercedValue = new JValue(dt.ToString("o"));
                    break;
                }
                default: // Object / Array — pass through uncast.
                    coercedValue = raw.DeepClone();
                    break;
            }

            return true;
        }

        private static JObject? ParseValidation(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var token = JToken.Parse(json);
                return token as JObject;
            }
            catch (JsonException)
            {
                return null; // malformed validation schema ⇒ no constraints enforced
            }
        }

        private static bool CheckStringConstraints(string s, JObject? v, out string error)
        {
            error = "";
            if (v != null)
            {
                if (v["minLength"]?.Type == JTokenType.Integer && s.Length < v["minLength"]!.Value<int>())
                    return Fail($"Must be at least {v["minLength"]} characters", out error);
                if (v["maxLength"]?.Type == JTokenType.Integer && s.Length > v["maxLength"]!.Value<int>())
                    return Fail($"Must be at most {v["maxLength"]} characters", out error);
                var pattern = v["pattern"]?.Type == JTokenType.String ? v["pattern"]!.Value<string>() : null;
                if (!string.IsNullOrEmpty(pattern) && !Regex.IsMatch(s, pattern))
                    return Fail("Does not match the required pattern", out error);
            }
            return true;

            static bool Fail(string message, out string err) { err = message; return false; }
        }

        private static bool CheckNumberConstraints(double n, JObject? v, out string error)
        {
            error = "";
            if (v != null)
            {
                if (v["minimum"]?.Type == JTokenType.Integer && n < v["minimum"]!.Value<double>())
                    return Fail($"Must be at least {v["minimum"]}", out error);
                if (v["maximum"]?.Type == JTokenType.Integer && n > v["maximum"]!.Value<double>())
                    return Fail($"Must be at most {v["maximum"]}", out error);
            }
            return true;

            static bool Fail(string message, out string err) { err = message; return false; }
        }
    }
}
