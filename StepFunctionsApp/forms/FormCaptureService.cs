using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Forms
{
    /// <summary>Read view of a suspended form-capture task: record metadata + bound form definition + attribute contract.
    /// Property names are camelCase on purpose — they ARE the wire format (the REST surface serializes this POCO
    /// directly and MCP tools serialize it with the same camelCase settings), matching the original anonymous object.</summary>
    public sealed class FormCaptureView
    {
        public string taskId { get; set; } = "";
        public string status { get; set; } = "";
        public string? title { get; set; }
        public string? assignee { get; set; }
        public string executionId { get; set; } = "";
        public string stateMachineName { get; set; } = "";
        public DateTime createdAtUtc { get; set; }
        public DateTime? completedAtUtc { get; set; }
        public JToken? result { get; set; }
        public string formUrl { get; set; } = "";
        public FormDefinition? form { get; set; }
        public List<EntityAttribute> attributes { get; set; } = new();
    }

    /// <summary>
    /// Runtime logic for suspended FormCapture states: load the bound form (version resolved at
    /// suspension time is authoritative), validate/coerce submissions against the bound attribute
    /// domain, persist captured EAV rows, then resume. Shared by the REST controller and the MCP
    /// tools so both surfaces behave identically. Outcomes carry an HTTP status + JSON body; the
    /// controller maps them straight to responses, MCP tools surface the body as tool output.
    /// </summary>
    public class FormCaptureService
    {
        private readonly IHumanTaskStore _taskStore;
        private readonly IFormDefinitionStore _formStore;
        private readonly IAttributeDomainStore _domainStore;
        private readonly EavRowStore _eavRows;
        private readonly StepFunctionService _stepService;

        public FormCaptureService(
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

        /// <summary>Form definition + bound attribute contract for a suspended FormCapture task.</summary>
        public async Task<(int Status, FormCaptureView? View, JObject? ErrorBody)> GetAsync(string taskId)
        {
            var (record, form) = await LoadFormTaskAsync(taskId);
            if (record == null) return (404, null, new JObject { ["error"] = $"Form capture task '{taskId}' not found" });
            if (form == null) return (500, null, new JObject { ["error"] = "The form bound to this task no longer exists in the definition store" });

            var attributes = new List<EntityAttribute>();
            if (!string.IsNullOrEmpty(form.AttributeDomainName))
            {
                var (domain, _) = _domainStore.GetByName(form.AttributeDomainName);
                if (domain != null) attributes.AddRange(domain.Attributes);
            }

            return (200, new FormCaptureView
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
                form = form,
                attributes = attributes
            }, null);
        }

        /// <summary>Validate + coerce the submission against the bound domain's attribute contract, persist an EAV row (bound forms only), then resume the suspended execution.</summary>
        public async Task<(int Status, JObject Body)> SubmitAsync(string taskId, JObject? body)
        {
            var (record, form) = await LoadFormTaskAsync(taskId);
            if (record == null) return (404, new JObject { ["error"] = $"Form capture task '{taskId}' not found" });
            if (form == null) return (500, new JObject { ["error"] = "The form bound to this task no longer exists in the definition store" });
            if (record.Status != HumanTaskStatus.Pending)
                return (409, new JObject { ["error"] = $"Form capture task '{taskId}' is already completed" });

            var valuesToken = body?["values"];
            if (valuesToken == null || valuesToken.Type != JTokenType.Object)
                return (400, new JObject { ["errors"] = new JObject { ["values"] = "Body must be an object with a 'values' JSON object" } });

            EntityAttribute[]? contract = null;
            if (!string.IsNullOrEmpty(form.AttributeDomainName))
            {
                var (domain, _) = _domainStore.GetByName(form.AttributeDomainName);
                contract = domain?.Attributes.Where(a => !string.IsNullOrWhiteSpace(a.AttributeName)).ToArray();
            }

            // Coercion + constraint validation lives in the StepFlow.Forms package (FormValidationService).
            var (coerced, errors) = FormValidationService.Validate(valuesToken, contract);
            if (errors.Count > 0) return (400, new JObject { ["errors"] = JObject.FromObject(errors) });

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
                if (completed == null) return (404, new JObject { ["error"] = $"Form capture task '{taskId}' not found" });
                return (200, new JObject { ["status"] = "completed", ["executionId"] = record.ExecutionId });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already"))
            {
                // Lost a race with another submit that completed the task first.
                return (409, new JObject { ["error"] = $"Form capture task '{taskId}' is already completed" });
            }
        }
    }
}
