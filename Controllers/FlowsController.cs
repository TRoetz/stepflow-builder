using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Controllers
{
    [ApiController]
    public class FlowsController : ControllerBase
    {
        private readonly StepFunctionService _stepService;

        public FlowsController(StepFunctionService stepService)
        {
            _stepService = stepService;
        }
        // Register/Save a state machine
        [HttpPost("api/flows")]
        [HttpPost("api/state-machines")]
        public IActionResult RegisterFlow([FromBody] JObject payload)
        {
            try
            {
                var name = payload["name"]?.ToString() ?? "New Flow";
                var description = payload["description"]?.ToString();
                
                // Parse definition
                var statesObj = payload["states"] as JObject ?? payload["definition"]?["states"] as JObject;

                if (statesObj == null)
                {
                    return BadRequest(new { error = "Payload must contain 'states' dictionary (Amazon States Language format)" });
                }

                var definition = new StateMachineDefinition
                {
                    StartAt = payload["startAt"]?.ToString() ?? payload["definition"]?["startAt"]?.ToString() ?? statesObj.Properties().FirstOrDefault()?.Name ?? "",
                    States = statesObj.ToObject<Dictionary<string, StateDefinition>>()!
                };

                var sm = _stepService.RegisterStateMachine(name, definition, description, payload["id"]?.ToString());
                return Ok(new
                {
                    id = sm.Id,
                    name = sm.Name,
                    description = sm.Description,
                    createdAt = sm.CreatedAt,
                    updatedAt = sm.UpdatedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
        // List state machines
        [HttpGet("api/flows")]
        [HttpGet("api/state-machines")]
        public IActionResult ListFlows()
        {
            var list = _stepService.ListStateMachines();
            return Ok(list.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                description = s.Description,
                createdAt = s.CreatedAt,
                updatedAt = s.UpdatedAt
            }));
        }

        // Get single state machine definition
        [HttpGet("api/flows/{id}/definition")]
        [HttpGet("api/state-machines/{id}")]
        public IActionResult GetFlow(string id)
        {
            var sm = _stepService.GetStateMachine(id);
            if (sm == null) return NotFound(new { error = $"Flow '{id}' not found" });
            return Ok(sm.Definition);
        }

        // Execute state machine asynchronously
        [HttpPost("api/flows/execute/{id}")]
        [HttpPost("api/state-machines/{id}/execute")]
        public IActionResult ExecuteFlowAsync(string id, [FromBody] JToken? input)
        {
            try
            {
                var execution = _stepService.StartExecution(id, input);
                return Ok(new
                {
                    executionId = execution.ExecutionId,
                    status = execution.Status.ToString(),
                    stateMachineId = execution.StateMachineId,
                    stateMachineName = execution.StateMachineName
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Execute state machine synchronously
        [HttpPost("api/flows/execute-sync/{id}")]
        public async Task<IActionResult> ExecuteFlowSync(string id, [FromBody] JToken? input)
        {
            try
            {
                var execution = await _stepService.ExecuteSyncAsync(id, input);
                return Ok(new
                {
                    executionId = execution.ExecutionId,
                    status = execution.Status.ToString(),
                    output = execution.Output,
                    errorCode = execution.ErrorCode,
                    errorMessage = execution.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Get execution status
        [HttpGet("api/flows/executions/{id}")]
        [HttpGet("api/execution/{id}")]
        public async Task<IActionResult> GetExecution(string id)
        {
            var execution = _stepService.GetExecution(id);
            if (execution == null)
            {
                // Fall back to the durable store so executions survive a backend restart.
                var record = await _stepService.LoadStoredExecutionAsync(id);
                if (record?.Execution == null) return NotFound(new { error = $"Execution '{id}' not found" });
                execution = record.Execution;
            }
            return Ok(new
            {
                executionId = execution.ExecutionId,
                stateMachineId = execution.StateMachineId,
                stateMachineName = execution.StateMachineName,
                status = execution.Status.ToString(),
                input = execution.Input,
                output = execution.Output,
                currentNode = execution.CurrentState,
                completedAt = execution.CompletedAt,
                errorCode = execution.ErrorCode,
                errorMessage = execution.ErrorMessage,
                history = execution.History
            });
        }

        // Stop execution
        [HttpPost("api/state-machines/{id}/stop")]
        public IActionResult StopExecution(string id)
        {
            // Just return success for mock stop, since background services handles it
            return Ok(new { message = "Execution stop requested" });
        }

        // List all stored executions (survives restarts; includes terminal history)
        [HttpGet("api/flows/executions")]
        public async Task<IActionResult> ListExecutions()
        {
            var summaries = await _stepService.ListStoredExecutionsAsync();
            return Ok(summaries);
        }

        // Resume a stored execution (Suspended, or recovered-but-not-auto-resumed)
        [HttpPost("api/flows/executions/{id}/resume")]
        public async Task<IActionResult> ResumeExecution(string id)
        {
            try
            {
                var execution = await _stepService.ResumeStoredExecutionAsync(id);
                return Ok(new { message = $"Execution '{id}' queued for resume", status = execution.Status.ToString() });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        // Delete a stored execution's checkpoint
        [HttpDelete("api/flows/executions/{id}")]
        public async Task<IActionResult> DeleteExecution(string id)
        {
            await _stepService.DeleteStoredExecutionAsync(id);
            return Ok(new { message = $"Execution '{id}' removed from the flow-state store" });
        }

        // Save and compile as a multi-file Project Structure on local disk
        [HttpPost("api/state-machines/save-project")]
        public async Task<IActionResult> SaveProject([FromBody] JObject payload)
        {
            try
            {
                var directoryPath = payload["directoryPath"]?.ToString();
                if (string.IsNullOrEmpty(directoryPath))
                {
                    directoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "StepFlowProject");
                }

                // Ensure directory exists
                Directory.CreateDirectory(directoryPath);

                var flowName = payload["name"]?.ToString() ?? "New Flow";

                // 1. project.json (Core metadata & pointers)
                var project = new JObject
                {
                    ["projectId"] = $"proj_{Guid.NewGuid():N}",
                    ["name"] = flowName,
                    ["version"] = "1.0.0",
                    ["layoutFile"] = "layout.json",
                    ["stepflowFile"] = "stepflow.json",
                    ["environmentsFile"] = "environments.json",
                    ["parametersFile"] = "parameters.json",
                    ["testSuiteFile"] = "test_suite.json",
                    ["activeEnvironment"] = "DEV"
                };
                await System.IO.File.WriteAllTextAsync(Path.Combine(directoryPath, "project.json"), project.ToString(Newtonsoft.Json.Formatting.Indented));

                // 2. layout.json (Coordinates & visual settings)
                var layout = new JObject
                {
                    ["nodes"] = payload["layout"]?["nodes"] ?? new JArray(),
                    ["edges"] = payload["layout"]?["edges"] ?? new JArray()
                };
                await System.IO.File.WriteAllTextAsync(Path.Combine(directoryPath, "layout.json"), layout.ToString(Newtonsoft.Json.Formatting.Indented));

                // 3. stepflow.json (Standard ASL definition)
                var stepflow = new JObject
                {
                    ["startAt"] = payload["stepflow"]?["startAt"] ?? "",
                    ["states"] = payload["stepflow"]?["states"] ?? new JObject()
                };
                await System.IO.File.WriteAllTextAsync(Path.Combine(directoryPath, "stepflow.json"), stepflow.ToString(Newtonsoft.Json.Formatting.Indented));

                // 4. environments.json (DEV/TEST/PROD configurations)
                var environments = new JObject
                {
                    ["DEV"] = new JObject { ["baseUrl"] = "https://api.exchangerate-api.com/v4/latest", ["tempDir"] = @"C:\temp\dev" },
                    ["TEST"] = new JObject { ["baseUrl"] = "https://test-api.exchangerate-api.com/v4/latest", ["tempDir"] = @"C:\temp\test" },
                    ["PROD"] = new JObject { ["baseUrl"] = "https://api.exchangerate-api.com/v4/latest", ["tempDir"] = @"C:\temp\prod" }
                };
                await System.IO.File.WriteAllTextAsync(Path.Combine(directoryPath, "environments.json"), environments.ToString(Newtonsoft.Json.Formatting.Indented));

                // 5. parameters.json (Mock inputs & test variables)
                var parameters = new JObject
                {
                    ["default"] = new JObject
                    {
                        ["baseVal"] = 100,
                        ["age"] = 25,
                        ["is_vip"] = 1,
                        ["total_spend"] = 600
                    }
                };
                await System.IO.File.WriteAllTextAsync(Path.Combine(directoryPath, "parameters.json"), parameters.ToString(Newtonsoft.Json.Formatting.Indented));

                // 6. test_suite.json (Step-by-step verification tests)
                var testSuite = new JObject
                {
                    ["tests"] = new JArray
                    {
                        new JObject
                        {
                            ["name"] = "Verify Step Execution",
                            ["nodeId"] = "stepflow:api:http",
                            ["input"] = new JObject { ["base"] = "NZD" },
                            ["expectedOutput"] = new JObject { ["base"] = "NZD" }
                        }
                    }
                };
                await System.IO.File.WriteAllTextAsync(Path.Combine(directoryPath, "test_suite.json"), testSuite.ToString(Newtonsoft.Json.Formatting.Indented));

                return Ok(new { success = true, message = $"Project compiled and saved to local directory: {directoryPath}", path = directoryPath });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Load a multi-file Project Structure from local disk
        [HttpPost("api/state-machines/load-project")]
        public async Task<IActionResult> LoadProject([FromBody] JObject payload)
        {
            try
            {
                var directoryPath = payload["directoryPath"]?.ToString();
                if (string.IsNullOrEmpty(directoryPath))
                {
                    return BadRequest(new { error = "Directory path is required." });
                }

                var projectPath = Path.Combine(directoryPath, "project.json");
                var layoutPath = Path.Combine(directoryPath, "layout.json");

                if (!System.IO.File.Exists(projectPath) || !System.IO.File.Exists(layoutPath))
                {
                    return NotFound(new { error = "Project files not found in the specified directory. Make sure project.json and layout.json exist." });
                }

                var projectContent = await System.IO.File.ReadAllTextAsync(projectPath);
                var layoutContent = await System.IO.File.ReadAllTextAsync(layoutPath);

                var project = JObject.Parse(projectContent);
                var layout = JObject.Parse(layoutContent);

                return Ok(new
                {
                    success = true,
                    name = project["name"]?.ToString() ?? "Loaded Flow",
                    layout = layout,
                    message = $"Project loaded successfully from: {directoryPath}"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
