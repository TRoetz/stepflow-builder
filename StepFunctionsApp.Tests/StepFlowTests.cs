using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using StepFunctionsApp.StepFunctions;
using StepFunctionsApp.DataExchange;
namespace StepFunctionsApp.Tests
{
    public class StepFlowTests
    {
        private readonly RuleEngineService _ruleEngine;
        private readonly MicrosoftRulesEngineService _msRulesEngine;
        private readonly DuckDbTransformService _duckDb;
        private readonly ScriptExecutionService _scriptExecution;
        private readonly AiDecisionService _aiDecision;
        private readonly CompositeResourceInvoker _resourceInvoker;
        private readonly StepFunctionInterpreter _interpreter;

        public StepFlowTests()
        {
            // Set up Null Loggers for fast, clutter-free test runs
            var ruleEngineLogger = NullLogger<RuleEngineService>.Instance;
            var msRulesLogger = NullLogger<MicrosoftRulesEngineService>.Instance;
            var duckDbLogger = NullLogger<DuckDbTransformService>.Instance;
            var scriptLogger = NullLogger<ScriptExecutionService>.Instance;
            var aiDecisionLogger = NullLogger<AiDecisionService>.Instance;
            var invokerLogger = NullLogger<CompositeResourceInvoker>.Instance;
            var interpreterLogger = NullLogger<StepFunctionInterpreter>.Instance;

            // Instantiate services
            _ruleEngine = new RuleEngineService(ruleEngineLogger);
            _msRulesEngine = new MicrosoftRulesEngineService(msRulesLogger);
            _duckDb = new DuckDbTransformService(duckDbLogger);
            _scriptExecution = new ScriptExecutionService(scriptLogger);
            
            // Mock HttpClientFactory for AI service
            var mockFactory = new Moq.Mock<System.Net.Http.IHttpClientFactory>();
            _aiDecision = new AiDecisionService(mockFactory.Object, aiDecisionLogger);

            // Circular reference Lazy resolver mock
            var mockStepService = new Moq.Mock<StepFunctionService>(null!, null!, null!, null!, null!, null!);
            var lazyStepService = new Lazy<StepFunctionService>(() => mockStepService.Object);

            // Data Exchange executor (profiles in an isolated temp dir)
            var dataExchange = new DataExchangeExecutor(
                new DataExchangeProfileStore(Path.Combine(Path.GetTempPath(), "dataexchange-tests", Guid.NewGuid().ToString("N"))),
                _duckDb,
                mockFactory.Object,
                NullLogger<DataExchangeExecutor>.Instance);

            _resourceInvoker = new CompositeResourceInvoker(
                mockFactory.Object,
                _ruleEngine,
                _msRulesEngine,
                _duckDb,
                new EavRegistryService(), // blank
                _scriptExecution,
                lazyStepService,
                dataExchange,
                new SshCommandService(new SshHostStore(), _aiDecision), // blank inventory; AI mocked via factory
                new FetchRemoteFilesService(new SshHostStore()), // blank inventory — fetch:// validation only
                invokerLogger
            );

            _interpreter = new StepFunctionInterpreter(_resourceInvoker, interpreterLogger);
        }

        // ── 1. TERMINAL & INTERPRETER TESTS ──────────────────────────────────
        [Fact]
        public async Task TestInterpreter_PassAndSucceedStates()
        {
            var definition = new StateMachineDefinition
            {
                StartAt = "StartNode",
                States = new Dictionary<string, StateDefinition>
                {
                    { "StartNode", new StateDefinition { Type = StateType.Pass, Next = "EndNode" } },
                    { "EndNode", new StateDefinition { Type = StateType.Succeed } }
                }
            };

            var input = JObject.FromObject(new { x = 1 });
            var execution = await _interpreter.ExecuteAsync(definition, input, "test-id", "flow-id");

            Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
            Assert.Equal("EndNode", execution.CurrentState);
        }

        [Fact]
        public async Task TestInterpreter_FailState()
        {
            var definition = new StateMachineDefinition
            {
                StartAt = "FailNode",
                States = new Dictionary<string, StateDefinition>
                {
                    { "FailNode", new StateDefinition { Type = StateType.Fail, Error = "CustomError", Cause = "Failed intentionally" } }
                }
            };

            var execution = await _interpreter.ExecuteAsync(definition, new JObject(), "test-id", "flow-id");

            Assert.Equal(ExecutionStatus.Failed, execution.Status);
            Assert.Equal("CustomError", execution.ErrorCode);
        }

        // ── 2. DATABASE & SQL RULES TESTS ────────────────────────────────────
        [Fact]
        public void TestSqlRulesEngine_ValidExpression()
        {
            var parameters = new Dictionary<string, object> { { "age", 25 } };
            var result = _ruleEngine.ExecuteRule("{age} >= 18 AND {age} <= 65", parameters);

            Assert.True(result.Status);
            Assert.False(result.HasErrored);
        }

        [Fact]
        public void TestSqlRulesEngine_InvalidExpression()
        {
            var parameters = new Dictionary<string, object> { { "age", 15 } };
            var result = _ruleEngine.ExecuteRule("{age} >= 18", parameters);

            Assert.False(result.Status);
        }

        // ── 3. DUCKDB TRANSFORM TESTS ────────────────────────────────────────
        [Fact]
        public void TestDuckDbTransform_SqlAggregation()
        {
            var input = JObject.FromObject(new { sql = "SELECT SUM(x) as total FROM (SELECT 10 as x UNION ALL SELECT 20 as x)" });
            var result = _duckDb.ExecuteTransform(input);

            var rows = result["rows"] as JArray;
            Assert.NotNull(rows);
            Assert.Single(rows);
            Assert.Equal("30", rows[0]?["total"]?.ToString());
        }

        // ── 4. JSONATA QUERY TESTS ───────────────────────────────────────────
        [Fact]
        public void TestJsonataProcessor_PathSelection()
        {
            var input = JObject.Parse("{ \"store\": { \"book\": [ { \"title\": \"Book A\", \"price\": 10 }, { \"title\": \"Book B\", \"price\": 15 } ] } }");
            
            // Execute Jsonata path querying
            var result = JsonataProcessor.Evaluate("store.book.price", input);
            
            Assert.IsType<JArray>(result);
            var prices = (JArray)result;
            Assert.Equal(10, prices[0]?.Value<int>());
            Assert.Equal(15, prices[1]?.Value<int>());
        }

        // ── 5. SCRIPTING TASKS TESTS ─────────────────────────────────────────
        [Fact]
        public async Task TestScriptExecution_JavaScript()
        {
            var input = JObject.FromObject(new { name = "stepflow" });
            var script = "return { message: 'Hello, ' + context.input.name };";
            
            var result = await _scriptExecution.ExecuteScriptAsync("javascript", script, input);
            Assert.Equal("Hello, stepflow", result["message"]?.ToString());
        }

        [Fact]
        public async Task TestScriptExecution_Python()
        {
            var input = JObject.FromObject(new { score = 90 });
            var script = "return { 'grade': 'A' if input['score'] >= 80 else 'B' }";

            var result = await _scriptExecution.ExecuteScriptAsync("python", script, input);
            Assert.Equal("A", result["grade"]?.ToString());
        }

        [Fact]
        public async Task TestScriptExecution_PowerShell()
        {
            var input = JObject.FromObject(new { baseVal = 100 });
            var script = "return [PSCustomObject]@{ result = $context.input.baseVal + 50 }";

            var result = await _scriptExecution.ExecuteScriptAsync("powershell", script, input);
            Assert.Equal(150, result["result"]?.Value<int>());
        }

        [Fact]
        public async Task TestScriptExecution_CSharpRoslyn()
        {
            var input = JObject.FromObject(new { x = 20, y = 30 });
            var script = "return new { sum = (int)input[\"x\"] + (int)input[\"y\"] };";

            var result = await _scriptExecution.ExecuteScriptAsync("csharp", script, input);
            Assert.Equal(50, result["sum"]?.Value<int>());
        }

        [Fact]
        public async Task TestScriptExecution_ShellCommand()
        {
            var command = OperatingSystem.IsWindows() ? "echo stepflow" : "echo -n stepflow";
            var result = await _scriptExecution.ExecuteScriptAsync("shell", command, new JObject());

            Assert.Contains("stepflow", result.ToString());
        }

        // ── 6. UTILITY FLOW TESTS ────────────────────────────────────────────
        [Fact]
        public async Task TestInterpreter_ChoiceState_BranchesCorrectly()
        {
            var definition = new StateMachineDefinition
            {
                StartAt = "CheckVal",
                States = new Dictionary<string, StateDefinition>
                {
                    { "CheckVal", new StateDefinition {
                        Type = StateType.Choice,
                        Choices = new List<ChoiceRule> {
                            new ChoiceRule { Variable = "$.val", StringEquals = "A", Next = "LeftPath" }
                        },
                        Default = "RightPath"
                    } },
                    { "LeftPath", new StateDefinition { Type = StateType.Pass, Next = "Done" } },
                    { "RightPath", new StateDefinition { Type = StateType.Pass, Next = "Done" } },
                    { "Done", new StateDefinition { Type = StateType.Succeed } }
                }
            };

            // Test input resolving choice condition to LeftPath
            var input = JObject.FromObject(new { val = "A" });
            var execution = await _interpreter.ExecuteAsync(definition, input, "test-id", "flow-id");
            Assert.Equal("Done", execution.CurrentState);
        }
    }
}
