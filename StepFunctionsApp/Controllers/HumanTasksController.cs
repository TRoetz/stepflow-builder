using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Controllers
{
    [ApiController]
    public class HumanTasksController : ControllerBase
    {
        private readonly StepFunctionService _stepService;
        private readonly IHumanTaskStore _store;

        public HumanTasksController(StepFunctionService stepService, IHumanTaskStore store)
        {
            _stepService = stepService;
            _store = store;
        }

        // List human tasks (optionally filtered by status or executionId)
        [HttpGet("api/human-tasks")]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? executionId)
        {
            var all = await _store.ListAsync();
            if (!string.IsNullOrEmpty(status))
                all = all.Where(t => t.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!string.IsNullOrEmpty(executionId))
                all = all.Where(t => t.ExecutionId == executionId).ToList();

            return Ok(all.Select(t => new
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
                completedAtUtc = t.CompletedAtUtc,
                payload = t.Payload
            }));
        }

        // Get one human task with its full payload and (if completed) result
        [HttpGet("api/human-tasks/{id}")]
        public async Task<IActionResult> Get(string id)
        {
            var task = await _store.LoadAsync(id);
            if (task == null) return NotFound(new { error = $"Human task '{id}' not found" });

            return Ok(new
            {
                taskId = task.TaskId,
                status = task.Status.ToString(),
                executionId = task.ExecutionId,
                stateMachineName = task.StateMachineName,
                stateName = task.StateName,
                nextState = task.NextState,
                isEnd = task.IsEnd,
                resultPath = task.ResultPath,
                title = task.Title,
                assignee = task.Assignee,
                completionType = task.CompletionType,
                createdAtUtc = task.CreatedAtUtc,
                completedAtUtc = task.CompletedAtUtc,
                payload = task.Payload,
                result = task.Result
            });
        }

        // Complete a pending human task with the human's result; resumes or terminates the execution.
        [HttpPost("api/human-tasks/{id}/complete")]
        public async Task<IActionResult> Complete(string id, [FromBody] JToken? result)
        {
            try
            {
                var task = await _stepService.CompleteHumanTaskAsync(id, result ?? new JObject());
                if (task == null) return NotFound(new { error = $"Human task '{id}' not found" });
                return Ok(new
                {
                    taskId = task.TaskId,
                    status = task.Status.ToString(),
                    executionId = task.ExecutionId,
                    nextState = task.NextState,
                    isEnd = task.IsEnd
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }
    }
}
