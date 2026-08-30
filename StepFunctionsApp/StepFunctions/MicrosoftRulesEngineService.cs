using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using RulesEngine;
using RulesEngine.Models;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // MICROSOFT RULES ENGINE SERVICE
    // Wraps Microsoft RulesEngine (github.com/microsoft/RulesEngine) for rich
    // rule composition: AND/OR/NOT, workflows, JSON-defined rules.
    //
    // Works alongside RuleEngineService (SQLite SQL rules) — use this for complex
    // business logic with multiple expressions, workflow mode, and rule trees.
    // ═══════════════════════════════════════════════════════════════════════════════

    public class MicrosoftRulesEngineService
    {
        private RulesEngine.RulesEngine _engine;
        private readonly ILogger<MicrosoftRulesEngineService> _logger;
        private readonly object _lock = new();
        private List<Workflow> _workflows = new();

        public MicrosoftRulesEngineService(ILogger<MicrosoftRulesEngineService> logger)
        {
            _logger = logger;
            _engine = new RulesEngine.RulesEngine(Array.Empty<Workflow>());
            _logger.LogInformation("MicrosoftRulesEngineService initialized");
        }

        // ── Workflow (Rule Set) Management ──────────────────────────────────────

        /// <summary>
        /// Register a named workflow (set of rules) from a JSON rule definition array.
        /// Rules use LambdaExpression syntax: "input1.field > 100 AND input1.status == 'active'"
        /// </summary>
        public void RegisterWorkflow(string workflowName, JArray rulesJson)
        {
            lock (_lock)
            {
                var rules = new List<Rule>();
                foreach (var ruleToken in rulesJson)
                {
                    var rule = new Rule
                    {
                        RuleName = ruleToken["RuleName"]?.ToString() ?? "Unnamed",
                        Expression = ruleToken["Expression"]?.ToString() ?? "true",
                        ErrorMessage = ruleToken["ErrorMessage"]?.ToString(),
                        SuccessEvent = ruleToken["SuccessEvent"]?.ToString(),
                        RuleExpressionType = RuleExpressionType.LambdaExpression,
                    };
                    rules.Add(rule);
                }

                var existing = _workflows.FirstOrDefault(w => w.WorkflowName == workflowName);
                if (existing != null)
                {
                    existing.Rules = rules;
                    _logger.LogInformation("Updated workflow: {Workflow} ({Rules} rules)", workflowName, rules.Count);
                }
                else
                {
                    var workflow = new Workflow { WorkflowName = workflowName, Rules = rules };
                    _workflows.Add(workflow);
                    _logger.LogInformation("Registered workflow: {Workflow} ({Rules} rules)", workflowName, rules.Count);
                }
                RebuildEngine();
            }
        }

        /// <summary>
        /// Register a workflow from a JSON file path or raw JSON string.
        /// </summary>
        public void RegisterWorkflowFromJson(string workflowName, string json)
        {
            var parsed = JToken.Parse(json);
            if (parsed is JArray arr)
            {
                RegisterWorkflow(workflowName, arr);
            }
            else if (parsed is JObject obj)
            {
                RegisterWorkflow(workflowName, JArray.FromObject(new[] { obj }));
            }
        }

        /// <summary>
        /// Remove a workflow by name.
        /// </summary>
        public void RemoveWorkflow(string workflowName)
        {
            lock (_lock)
            {
                _workflows.RemoveAll(w => w.WorkflowName == workflowName);
                RebuildEngine();
                _logger.LogInformation("Removed workflow: {Workflow}", workflowName);
            }
        }

        /// <summary>
        /// List all registered workflow names.
        /// </summary>
        public List<string> ListWorkflows()
        {
            lock (_lock) { return _workflows.Select(w => w.WorkflowName).ToList(); }
        }

        /// <summary>
        /// Get a workflow definition by name.
        /// </summary>
        public Workflow? GetWorkflow(string workflowName)
        {
            lock (_lock) { return _workflows.FirstOrDefault(w => w.WorkflowName == workflowName); }
        }

        // ── Rule Execution ──────────────────────────────────────────────────────

        /// <summary>
        /// Execute a workflow against input data and return the result.
        /// Input is passed as "input1" in rule expressions.
        /// JObject values are converted to native C# types for expression evaluation.
        /// </summary>
        public RuleExecutionResult ExecuteWorkflow(string workflowName, JObject inputData)
        {
            lock (_lock)
            {
                try
                {
                    var typedInput = JObjectToDictionary(inputData);
                    var results = _engine.ExecuteAllRulesAsync(workflowName, typedInput).Result;
                    return MapResults(workflowName, results);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Workflow execution failed for '{Workflow}'", workflowName);
                    return new RuleExecutionResult
                    {
                        WorkflowName = workflowName,
                        Success = false,
                        ErrorMessage = ex.Message,
                        RuleResults = new()
                    };
                }
            }
        }

        /// <summary>
        /// Convert JObject to Dictionary<string,object> with properly typed values
        /// so RulesEngine lambda expressions can compare them.
        /// </summary>
        private Dictionary<string, object?> JObjectToDictionary(JObject obj)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in obj.Properties())
            {
                dict[prop.Name] = prop.Value.ToObject<object>();
            }
            return dict;
        }

        /// <summary>
        /// Execute a single inline expression against input data.
        /// Expression uses LambdaExpression syntax: "input1.field > 100"
        /// </summary>
        public RuleExecutionResult ExecuteExpression(string expression, JObject inputData)
        {
            lock (_lock)
            {
                try
                {
                    var tempWorkflow = new Workflow
                    {
                        WorkflowName = "_temp_expr",
                        Rules = new List<Rule>
                        {
                            new()
                            {
                                RuleName = "InlineExpression",
                                Expression = expression,
                                RuleExpressionType = RuleExpressionType.LambdaExpression
                            }
                        }
                    };

                    var allWorkflows = _workflows.Concat(new[] { tempWorkflow }).ToArray();
                    var tempEngine = new RulesEngine.RulesEngine(allWorkflows);
                    var typedInput = JObjectToDictionary(inputData);
                    var results = tempEngine.ExecuteAllRulesAsync("_temp_expr", typedInput).Result;
                    return MapResults("_temp_expr", results);
                }
                catch (Exception ex)
                {
                    return new RuleExecutionResult
                    {
                        WorkflowName = "_temp_expr",
                        Success = false,
                        ErrorMessage = ex.Message,
                        RuleResults = new()
                    };
                }
            }
        }

        /// <summary>
        /// Execute a workflow and return true if ALL rules passed, false otherwise.
        /// </summary>
        public bool ExecuteAsBoolean(string workflowName, JObject inputData)
        {
            var result = ExecuteWorkflow(workflowName, inputData);
            return result.AllPassed;
        }

        // ── Helper Methods ──────────────────────────────────────────────────────

        private RuleExecutionResult MapResults(string workflowName, List<RuleResultTree>? results)
        {
            if (results == null || results.Count == 0)
                return new RuleExecutionResult
                {
                    WorkflowName = workflowName,
                    Success = false,
                    ErrorMessage = "No results returned",
                    RuleResults = new()
                };

            return new RuleExecutionResult
            {
                WorkflowName = workflowName,
                Success = results.All(r => r.IsSuccess),
                ErrorMessage = results.FirstOrDefault(r => !r.IsSuccess)?.ExceptionMessage,
                RuleResults = results.Select(r => new RuleExecutionDetail
                {
                    RuleName = r.Rule?.RuleName ?? "Unknown",
                    Result = r.IsSuccess,
                    Error = r.ExceptionMessage,
                    Exception = r.ExceptionMessage
                }).ToList()
            };
        }

        private void RebuildEngine()
        {
            _engine = new RulesEngine.RulesEngine(_workflows.ToArray());
        }

        public object GetStatus()
        {
            lock (_lock)
            {
                return new
                {
                    workflowsRegistered = _workflows.Count,
                    workflows = _workflows.Select(w => new
                    {
                        name = w.WorkflowName,
                        ruleCount = w.Rules != null ? System.Linq.Enumerable.Count(w.Rules) : 0
                    }).ToList()
                };
            }
        }
    }

    // ── Result Models ───────────────────────────────────────────────────────────

    public class RuleExecutionResult
    {
        [JsonProperty("workflowName")]
        public string WorkflowName { get; set; } = "";

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }

        [JsonProperty("ruleResults")]
        public List<RuleExecutionDetail> RuleResults { get; set; } = new();

        /// <summary>True if all rules passed (no errors, all expressions true).</summary>
        [JsonProperty("allPassed")]
        public bool AllPassed => Success && RuleResults.All(r => r.Result);
    }

    public class RuleExecutionDetail
    {
        [JsonProperty("ruleName")]
        public string RuleName { get; set; } = "";

        [JsonProperty("result")]
        public bool Result { get; set; }

        [JsonProperty("error")]
        public string? Error { get; set; }

        [JsonProperty("exception")]
        public string? Exception { get; set; }
    }
}
