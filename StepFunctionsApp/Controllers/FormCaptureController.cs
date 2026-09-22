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

            // Coercion + constraint validation lives in the StepFlow.Forms package (FormValidationService).
            var (coerced, errors) = FormValidationService.Validate(valuesToken, contract);
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

    }
}
