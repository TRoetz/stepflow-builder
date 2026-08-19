using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;

namespace Stepflow_Builder_Tests.Controllers
{
    public class TestResultItem
    {
        public string Name { get; set; } = "";
        public bool Passed { get; set; }
        public string? Output { get; set; }
        public string? Error { get; set; }
        public long DurationMs { get; set; }
    }

    [ApiController]
    [Route("api/tests")]
    public class TestsController : ControllerBase
    {
        private readonly DuckDbTransformService _duckDb;
        private readonly ScriptExecutionService _scriptExecution;
        private readonly RuleEngineService _ruleEngine;
        private readonly MicrosoftRulesEngineService _msRulesEngine;

        public TestsController(
            DuckDbTransformService duckDb,
            ScriptExecutionService scriptExecution,
            RuleEngineService ruleEngine,
            MicrosoftRulesEngineService msRulesEngine)
        {
            _duckDb = duckDb;
            _scriptExecution = scriptExecution;
            _ruleEngine = ruleEngine;
            _msRulesEngine = msRulesEngine;
        }

        [HttpGet("run")]
        public async Task<IActionResult> RunAllTests()
        {
            var results = new List<TestResultItem>();

            // 1. DuckDB Transform Test
            results.Add(await RunTest("DuckDB SQL Query", async () =>
            {
                var input = JObject.FromObject(new { sql = "SELECT 1 + 1 as result" });
                var res = _duckDb.ExecuteTransform(input);
                var val = res[0]?["result"]?.Value<int>();
                if (val != 2) throw new Exception($"Expected 2, got {val}");
            }));

            // 2. JavaScript (Node) Script Test
            results.Add(await RunTest("JavaScript (Node.js) Execution", async () =>
            {
                var input = JObject.FromObject(new { value = 10 });
                var script = "return { doubleValue: context.input.value * 2 };";
                var res = await _scriptExecution.ExecuteScriptAsync("javascript", script, input);
                var doubleVal = res["doubleValue"]?.Value<int>();
                if (doubleVal != 20) throw new Exception($"Expected 20, got {doubleVal}");
            }));

            // 3. Python Script Test
            results.Add(await RunTest("Python Script Execution", async () =>
            {
                var input = JObject.FromObject(new { val = 5 });
                var script = "return { 'triple': input['val'] * 3 }";
                var res = await _scriptExecution.ExecuteScriptAsync("python", script, input);
                var triple = res["triple"]?.Value<int>();
                if (triple != 15) throw new Exception($"Expected 15, got {triple}");
            }));

            // 4. PowerShell Script Test
            results.Add(await RunTest("PowerShell Script Execution", async () =>
            {
                var input = JObject.FromObject(new { x = 7 });
                var script = "return [PSCustomObject]@{ result = $input.x + 3 }";
                var res = await _scriptExecution.ExecuteScriptAsync("powershell", script, input);
                var resultVal = res["result"]?.Value<int>();
                if (resultVal != 10) throw new Exception($"Expected 10, got {resultVal}");
            }));

            // 5. C# Roslyn Script Test
            results.Add(await RunTest("C# Roslyn Script Execution", async () =>
            {
                var input = JObject.FromObject(new { val = 8 });
                var script = "return new { squared = (int)input[\"val\"] * (int)input[\"val\"] };";
                var res = await _scriptExecution.ExecuteScriptAsync("csharp", script, input);
                var squared = res["squared"]?.Value<int>();
                if (squared != 64) throw new Exception($"Expected 64, got {squared}");
            }));

            // 6. Shell Command (Bash/CMD) Test
            results.Add(await RunTest("Shell Command Execution", async () =>
            {
                var input = new JObject();
                var command = OperatingSystem.IsWindows() ? "echo 42" : "echo -n 42";
                var res = await _scriptExecution.ExecuteScriptAsync("shell", command, input);
                
                // On Windows echo outputs with newline, so check substring
                var val = res.ToString().Trim();
                if (!val.Contains("42")) throw new Exception($"Expected to contain '42', got '{val}'");
            }));

            // 7. Rules Engine Test
            results.Add(await RunTest("SQLite SQL Rules Engine", () =>
            {
                var parameters = new Dictionary<string, object> { { "score", 85 } };
                var ruleRes = _ruleEngine.ExecuteRule("score >= 80", parameters);
                if (!ruleRes.Status) throw new Exception("Rule 'score >= 80' failed for score=85");
                return Task.CompletedTask;
            }));

            // Calculate summaries
            int passedCount = results.FindAll(r => r.Passed).Count;
            return Ok(new
            {
                totalTests = results.Count,
                passed = passedCount,
                failed = results.Count - passedCount,
                report = results
            });
        }

        private async Task<TestResultItem> RunTest(string name, Func<Task> testAction)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var item = new TestResultItem { Name = name };

            try
            {
                await testAction();
                item.Passed = true;
                item.Output = "Passed successfully";
            }
            catch (Exception ex)
            {
                item.Passed = false;
                item.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                item.DurationMs = sw.ElapsedMilliseconds;
            }

            return item;
        }
    }
}
