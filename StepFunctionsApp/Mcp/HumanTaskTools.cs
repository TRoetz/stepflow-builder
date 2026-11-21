using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Mcp
{
    // ═══════════════════════════════════════════════════════════════════
    // MCP HUMAN-TASK TOOLS
    // Flows suspend at HumanTask states and wait for external completion. These tools let an AI
    // harness drive that loop headlessly: list pending tasks, inspect the payload snapshot, then
    // complete a task (resuming or terminating its execution). Form-bound tasks should use
    // submit_form_capture instead — it validates values against the bound attribute domain and
    // persists an EAV row before resuming.
    // ═══════════════════════════════════════════════════════════════════

    [McpServerToolType]
    public class HumanTaskTools
    {
        private readonly IHumanTaskStore _store;
        private readonly StepFunctionService _service;

        public HumanTaskTools(IHumanTaskStore store, StepFunctionService service)
        {
            _store = store;
            _service = service;
        }

        [McpServerTool]
        [Description(
"""
List human tasks — flows suspended at a HumanTask state waiting for external completion. Optionally filter by status (Pending or Completed) and/or executionId. Returns taskId, status, executionId, stateMachineName, stateName, nextState, isEnd, title, assignee, completionType and timestamps for each task.
""")]
        public async Task<string> ListHumanTasks(
            [Description("Optional status filter: 'Pending' or 'Completed'. Omit to list all.")] string? status = null,
            [Description("Optional execution id filter (as returned by run_flow).")] string? executionId = null)
        {
            var all = await _store.ListAsync();
            if (!string.IsNullOrEmpty(status))
                all = all.Where(t => t.Status.ToString().Equals(status, System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (!string.IsNullOrEmpty(executionId))
                all = all.Where(t => t.ExecutionId == executionId).ToList();

            return JsonConvert.SerializeObject(all.Select(Summarize), McpJson.Settings);
        }

        [McpServerTool]
        [Description(
"""
Get one human task with its full payload (the state's Task config plus a snapshot of the flow input at suspension time) and, once completed, the completion result. Use list_human_tasks to find task ids.
""")]
        public async Task<string> GetHumanTask([Description("Task id")] string taskId)
        {
            var record = await _store.LoadAsync(taskId);
            if (record == null) return Error($"Human task '{taskId}' not found. Use list_human_tasks to see pending tasks.");

            return JsonConvert.SerializeObject(new
            {
                record.TaskId,
                Status = record.Status.ToString(),
                record.ExecutionId,
                record.StateMachineName,
                record.StateName,
                record.NextState,
                record.IsEnd,
                record.ResultPath,
                record.Title,
                record.Assignee,
                record.CompletionType,
                record.Payload,
                record.CreatedAtUtc,
                record.CompletedAtUtc,
                record.Result
            }, McpJson.Settings);
        }

        [McpServerTool]
        [Description(
"""
Complete a pending human task with the given result; this resumes (or terminates) its suspended execution. Without a ResultPath on the state the result replaces the flow input entirely; with one it is merged at that JSON path. For form-bound tasks prefer submit_form_capture, which validates values against the bound attribute domain and persists an EAV row before resuming. The resumed run continues in the background — poll get_execution for the final status and output.
""")]
        public async Task<string> CompleteHumanTask(
            [Description("Task id (from list_human_tasks).")] string taskId,
            [Description("Optional completion result as a JSON value, e.g. {\"approved\": true}. Defaults to {} when omitted.")] string? resultJson = null)
        {
            JToken result;
            try
            {
                result = string.IsNullOrWhiteSpace(resultJson) ? new JObject() : JToken.Parse(resultJson);
            }
            catch (Exception ex)
            {
                return Error($"result is not valid JSON: {ex.Message}");
            }

            try
            {
                var task = await _service.CompleteHumanTaskAsync(taskId, result);
                if (task == null) return Error($"Human task '{taskId}' not found. Use list_human_tasks to see pending tasks.");

                return JsonConvert.SerializeObject(new
                {
                    taskId = task.TaskId,
                    status = task.Status.ToString(),
                    executionId = task.ExecutionId,
                    nextState = task.NextState,
                    isEnd = task.IsEnd
                }, McpJson.Settings);
            }
            catch (InvalidOperationException ex)
            {
                return Error(ex.Message);
            }
        }

        private static object Summarize(HumanTaskRecord t) => new
        {
            taskId = t.TaskId,
            status = t.Status.ToString(),
            executionId = t.ExecutionId,
            stateMachineName = t.StateMachineName,
            stateName = t.StateName,
            nextState = t.NextState,
            isEnd = t.IsEnd,
            title = t.Title,
            assignee = t.Assignee,
            completionType = t.CompletionType,
            createdAtUtc = t.CreatedAtUtc,
            completedAtUtc = t.CompletedAtUtc
        };

        private static string Error(string message) => JsonConvert.SerializeObject(new { error = message });
    }
}
