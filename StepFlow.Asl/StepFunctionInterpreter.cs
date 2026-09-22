using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // STEP FUNCTION INTERPRETER
    // Executes Amazon States Language state machines locally.
    // ═══════════════════════════════════════════════════════════════════════════════

    public class StepFunctionInterpreter
    {
        private readonly IResourceInvoker _resourceInvoker;
        private readonly ILogger<StepFunctionInterpreter> _logger;
        private readonly IHumanTaskStore? _humanTaskStore;
        private readonly IFormDefinitionStore? _formStore;

        public StepFunctionInterpreter(
            IResourceInvoker resourceInvoker,
            ILogger<StepFunctionInterpreter> logger,
            IHumanTaskStore? humanTaskStore = null,
            IFormDefinitionStore? formStore = null)
        {
            _resourceInvoker = resourceInvoker;
            _logger = logger;
            _humanTaskStore = humanTaskStore;
            _formStore = formStore;
        }

        /// <summary>
        /// Invoked at every state boundary — before each state executes, and once more on terminal status —
        /// to persist a checkpoint. The callback must serialize the execution immediately; it may throw, in
        /// which case the failure propagates to the caller (the service degrades gracefully).
        /// </summary>
        public delegate Task CheckpointCallback(Execution execution);

        public async Task<Execution> ExecuteAsync(
            StateMachineDefinition definition,
            JToken input,
            string executionId,
            string stateMachineId,
            CancellationToken cancellationToken = default,
            CheckpointCallback? onCheckpoint = null)
        {
            var execution = new Execution
            {
                ExecutionId = executionId,
                StateMachineId = stateMachineId,
                Input = input,
                Status = ExecutionStatus.Running,
                CurrentState = definition.StartAt
            };

            return await RunCoreAsync(definition, execution, input.DeepClone(), cancellationToken, onCheckpoint);
        }

        /// <summary>
        /// Resumes a previously checkpointed execution from its CurrentState using PendingInput.
        /// Completed states are not re-executed; the in-flight state (if any) may re-execute (at-least-once).
        /// </summary>
        public async Task<Execution> ResumeAsync(
            StateMachineDefinition definition,
            Execution execution,
            CancellationToken cancellationToken = default,
            CheckpointCallback? onCheckpoint = null)
        {
            if (string.IsNullOrEmpty(execution.CurrentState))
                throw new StepEngineException("States.Resume", "Execution has no pending state to resume from");

            var stateInput = execution.PendingInput?.DeepClone() ?? new JObject();
            return await RunCoreAsync(definition, execution, stateInput, cancellationToken, onCheckpoint);
        }

        private async Task<Execution> RunCoreAsync(
            StateMachineDefinition definition,
            Execution execution,
            JToken stateInput,
            CancellationToken cancellationToken,
            CheckpointCallback? onCheckpoint)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (definition.TimeoutSeconds.HasValue)
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(definition.TimeoutSeconds.Value));

            // Resume from a Suspended/Running checkpoint: the loop runs while Status == Running.
            execution.Status = ExecutionStatus.Running;

            try
            {
                while (execution.Status == ExecutionStatus.Running)
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();

                    var stateName = execution.CurrentState!;
                    if (!definition.States.TryGetValue(stateName, out var state))
                    {
                        throw new StepEngineException("States.Runtime", $"State '{stateName}' not found in definition");
                    }

                    // Checkpoint boundary: CurrentState + PendingInput describe exactly where to resume.
                    // Saved before StateEntered so recovery re-enters the in-flight state (at-least-once).
                    execution.PendingInput = stateInput.DeepClone();
                    if (onCheckpoint != null) await onCheckpoint(execution);
                    _logger.LogDebug("Executing state: {StateName} (Type: {Type})", stateName, state.Type);
                    AddEvent(execution, "StateEntered", stateName, new JObject { ["input"] = Truncate(stateInput) });

                    var sw = Stopwatch.StartNew();
                    JToken stateOutput;

                    try
                    {
                        stateOutput = state.Type switch
                        {
                            StateType.Task => await ExecuteTaskState(state, stateInput, execution, definition.QueryLanguage, timeoutCts.Token),
                            StateType.Pass => ExecutePassState(state, stateInput, definition.QueryLanguage),
                            StateType.Choice => ExecuteChoiceState(state, stateInput, execution, definition.QueryLanguage),
                            StateType.Wait => await ExecuteWaitState(state, stateInput, definition.QueryLanguage, timeoutCts.Token),
                            StateType.HumanTask => await ExecuteHumanTaskState(state, stateInput, execution, definition.QueryLanguage),
                            StateType.FormCapture => await ExecuteFormCaptureState(state, stateInput, execution, definition.QueryLanguage),
                            StateType.Succeed => ExecuteSucceedState(state, stateInput, definition.QueryLanguage, execution),
                            StateType.Fail => ExecuteFailState(state, execution),
                            StateType.Parallel => await ExecuteParallelState(state, stateInput, execution, definition.QueryLanguage, timeoutCts.Token),
                            StateType.Map => await ExecuteMapState(state, stateInput, execution, definition.QueryLanguage, timeoutCts.Token),
                            _ => throw new StepEngineException("States.Runtime", $"Unknown state type: {state.Type}")
                        };
                    }
                    catch (StepEngineException ex) when (state.Catch != null)
                    {
                        stateOutput = HandleCatch(state, stateInput, ex, execution, out var nextState);
                        if (nextState != null)
                        {
                            execution.CurrentState = nextState;
                            stateInput = stateOutput;
                            continue;
                        }
                        throw;
                    }

                    sw.Stop();
                    AddEvent(execution, "StateExited", stateName,
                        new JObject { ["output"] = Truncate(stateOutput) },
                        sw.Elapsed.TotalMilliseconds);

                    if (execution.Status != ExecutionStatus.Running)
                        break;

                    if (state.Type == StateType.Choice)
                    {
                        stateInput = stateOutput;
                        continue;
                    }

                    if (state.End == true)
                    {
                        execution.Status = ExecutionStatus.Succeeded;
                        execution.Output = stateOutput;
                        break;
                    }

                    if (!string.IsNullOrEmpty(state.Next))
                    {
                        execution.CurrentState = state.Next;
                        stateInput = stateOutput;
                    }
                    else
                    {
                        throw new StepEngineException("States.Runtime", $"State '{stateName}' has no Next and is not an End state");
                    }
                }
            }
            catch (ExecutionSuspendedException ex)
            {
                // A human task is waiting for external completion. Point at the state to run on resume;
                // PendingInput keeps the input heading into the human task so completion can merge against it.
                execution.Status = ExecutionStatus.Suspended;
                if (!string.IsNullOrEmpty(ex.NextState)) execution.CurrentState = ex.NextState;
                AddEvent(execution, "HumanTaskWaiting", ex.StateName, new JObject { ["taskId"] = ex.TaskId });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Definition-level timeout expired.
                execution.Status = ExecutionStatus.TimedOut;
                execution.ErrorCode = "States.Timeout";
                execution.ErrorMessage = "Execution timed out";
                AddEvent(execution, "ExecutionTimedOut", execution.CurrentState ?? "");
            }
            catch (OperationCanceledException)
            {
                // Host shutdown or caller cancellation: the process is going away. Leave Status as-is and skip the terminal checkpoint so the last boundary record in the store stays authoritative — recovery on next startup resumes from there.
                return execution;
            }
            catch (StepEngineException ex)
            {
                execution.Status = ExecutionStatus.Failed;
                execution.ErrorCode = ex.ErrorCode;
                execution.ErrorMessage = ex.Message;
                AddEvent(execution, "ExecutionFailed", execution.CurrentState ?? "", new JObject { ["error"] = ex.ErrorCode, ["cause"] = ex.Message });
            }
            catch (Exception ex)
            {
                execution.Status = ExecutionStatus.Failed;
                execution.ErrorCode = "States.Runtime";
                execution.ErrorMessage = ex.Message;
                AddEvent(execution, "ExecutionFailed", execution.CurrentState ?? "", new JObject { ["error"] = "States.Runtime", ["cause"] = ex.Message });
            }

            if (execution.Status != ExecutionStatus.Suspended) execution.CompletedAt = DateTime.UtcNow;
            // Terminal record so the store reflects the final status.
            if (onCheckpoint != null) await onCheckpoint(execution);
            return execution;
        }

        private async Task<JToken> ExecuteTaskState(StateDefinition state, JToken input, Execution execution, string queryLanguage, CancellationToken ct)
        {
            var effectiveInput = ApplyInputPath(state, input, queryLanguage);
            effectiveInput = ApplyParameters(state, effectiveInput, input, execution, queryLanguage);

            var resource = state.Resource ?? throw new StepEngineException("States.Runtime", "Task state missing Resource");

            JToken result;
            var retryRules = state.Retry ?? new();

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    AddEvent(execution, "TaskStarted", execution.CurrentState!, new JObject { ["resource"] = resource, ["attempt"] = attempt + 1 });

                    using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    if (state.TimeoutSeconds.HasValue)
                        taskCts.CancelAfter(TimeSpan.FromSeconds(state.TimeoutSeconds.Value));

                    result = await _resourceInvoker.InvokeAsync(resource, effectiveInput, taskCts.Token);

                    AddEvent(execution, "TaskSucceeded", execution.CurrentState!, new JObject { ["output"] = Truncate(result) });
                    break;
                }
                catch (Exception ex) when (attempt < GetMaxRetries(retryRules, ex))
                {
                    var delay = GetRetryDelay(retryRules, ex, attempt);
                    _logger.LogWarning("Task retry {Attempt} for {State}, waiting {Delay}ms", attempt + 1, execution.CurrentState, delay);
                    AddEvent(execution, "TaskRetrying", execution.CurrentState!, new JObject { ["attempt"] = attempt + 1, ["error"] = ex.Message });
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException) when (state.TimeoutSeconds.HasValue)
                {
                    throw new StepEngineException("States.Timeout", $"Task timed out after {state.TimeoutSeconds}s");
                }
            }

            result = ApplyResultSelector(state, result, queryLanguage);
            var output = ApplyResultPath(state, input, result, queryLanguage);
            output = ApplyOutputPath(state, output, queryLanguage);
            return output;
        }

        private async Task<JToken> ExecuteHumanTaskState(StateDefinition state, JToken input, Execution execution, string queryLanguage)
        {
            if (_humanTaskStore == null)
                throw new StepEngineException("States.Runtime", "HumanTask states require a human task store (not configured)");

            if (state.End != true && string.IsNullOrEmpty(state.Next))
                throw new StepEngineException("States.Runtime", $"HumanTask state '{execution.CurrentState}' has no Next and is not an End state");

            var effectiveInput = ApplyInputPath(state, input, queryLanguage);
            effectiveInput = ApplyParameters(state, effectiveInput, input, execution, queryLanguage);

            var taskId = Guid.NewGuid().ToString("N")[..8];
            var record = new HumanTaskRecord
            {
                TaskId = taskId,
                ExecutionId = execution.ExecutionId,
                StateMachineName = execution.StateMachineName ?? "",
                StateName = execution.CurrentState!,
                NextState = state.Next,
                IsEnd = state.End == true,
                ResultPath = state.ResultPath,
                Title = state.Task?["title"]?.ToString(),
                Assignee = state.Task?["assignee"]?.ToString(),
                Payload = new JObject { ["task"] = state.Task ?? new JObject(), ["input"] = effectiveInput },
                CompletionType = (state.Completion?["Type"]?.ToString() ?? "api").ToLowerInvariant(),
                CompletionConfig = state.Completion,
                Status = HumanTaskStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };

            await _humanTaskStore.SaveAsync(record);
            AddEvent(execution, "HumanTaskCreated", execution.CurrentState!, new JObject { ["taskId"] = taskId, ["completionType"] = record.CompletionType });
            throw new ExecutionSuspendedException(execution.CurrentState!, taskId, state.Next);
        }

        private async Task<JToken> ExecuteFormCaptureState(StateDefinition state, JToken input, Execution execution, string queryLanguage)
        {
            if (_humanTaskStore == null)
                throw new StepEngineException("States.Runtime", "FormCapture states require a human task store (not configured)");
            if (_formStore == null)
                throw new StepEngineException("States.Runtime", "FormCapture states require a form definition store (not configured)");

            if (state.End != true && string.IsNullOrEmpty(state.Next))
                throw new StepEngineException("States.Runtime", $"FormCapture state '{execution.CurrentState}' has no Next and is not an End state");

            var formId = state.Task?["formId"]?.ToString();
            if (string.IsNullOrWhiteSpace(formId))
                throw new StepEngineException("FormCapture.Config", $"FormCapture state '{execution.CurrentState}' requires Task.formId");

            // Version pinning: an explicit Task.formVersion resolves that exact saved version;
            // otherwise the form's current version is used. The resolved version is recorded on
            // the human task so a suspended fill-in page keeps working if the form is republished.
            var pinnedVersion = state.Task?["formVersion"]?.ToString();
            FormDefinition form;
            if (!string.IsNullOrWhiteSpace(pinnedVersion))
            {
                form = _formStore.Get(formId, pinnedVersion);
                if (form == null)
                    throw new StepEngineException("FormCapture.FormNotFound", $"Form '{formId}' version '{pinnedVersion}' not found in definition store");
            }
            else
            {
                form = _formStore.Get(formId);
                if (form == null)
                    throw new StepEngineException("FormCapture.FormNotFound", $"Form '{formId}' not found in definition store");
            }

            var effectiveInput = ApplyInputPath(state, input, queryLanguage);
            effectiveInput = ApplyParameters(state, effectiveInput, input, execution, queryLanguage);

            var taskId = Guid.NewGuid().ToString("N")[..8];
            var record = new HumanTaskRecord
            {
                TaskId = taskId,
                ExecutionId = execution.ExecutionId,
                StateMachineName = execution.StateMachineName ?? "",
                StateName = execution.CurrentState!,
                NextState = state.Next,
                IsEnd = state.End == true,
                ResultPath = state.ResultPath,
                FormVersion = form.Version,
                Title = state.Task?["title"]?.ToString() ?? form.Title,
                Assignee = state.Task?["assignee"]?.ToString(),
                Payload = new JObject { ["task"] = state.Task ?? new JObject(), ["input"] = effectiveInput },
                CompletionType = "form",
                CompletionConfig = state.Completion ?? new JObject { ["Type"] = "form" },
                Status = HumanTaskStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };

            await _humanTaskStore.SaveAsync(record);
            AddEvent(execution, "FormCaptureCreated", execution.CurrentState!, new JObject { ["taskId"] = taskId, ["formId"] = formId, ["formVersion"] = form.Version });
            throw new ExecutionSuspendedException(execution.CurrentState!, taskId, state.Next);
        }

        private JToken ExecutePassState(StateDefinition state, JToken input, string queryLanguage)
        {
            var effectiveInput = ApplyInputPath(state, input, queryLanguage);
            var result = state.Result ?? effectiveInput;
            result = ApplyResultSelector(state, result, queryLanguage);
            var output = ApplyResultPath(state, input, result, queryLanguage);
            return ApplyOutputPath(state, output, queryLanguage);
        }

        private JToken ExecuteChoiceState(StateDefinition state, JToken input, Execution execution, string queryLanguage)
        {
            var effectiveInput = ApplyInputPath(state, input, queryLanguage);
            if (state.Choices == null || state.Choices.Count == 0) throw new StepEngineException("States.Runtime", "Choice state has no choices");

            foreach (var choice in state.Choices)
            {
                if (EvaluateChoiceRule(choice, effectiveInput, queryLanguage))
                {
                    execution.CurrentState = choice.Next ?? throw new StepEngineException("States.Runtime", "Matching choice has no Next");
                    return ApplyOutputPath(state, effectiveInput, queryLanguage);
                }
            }

            if (!string.IsNullOrEmpty(state.Default))
            {
                execution.CurrentState = state.Default;
                return ApplyOutputPath(state, effectiveInput, queryLanguage);
            }

            throw new StepEngineException("States.NoChoiceMatched", "No choice rule matched and no Default specified");
        }

        private bool EvaluateChoiceRule(ChoiceRule rule, JToken input, string queryLanguage)
        {
            if (!string.IsNullOrWhiteSpace(rule.Expression)) return IsTruthy(JsonataProcessor.Evaluate(rule.Expression, input));

            if (rule.And != null) return rule.And.All(r => EvaluateChoiceRule(r, input, queryLanguage));
            if (rule.Or != null) return rule.Or.Any(r => EvaluateChoiceRule(r, input, queryLanguage));
            if (rule.Not != null) return !EvaluateChoiceRule(rule.Not, input, queryLanguage);

            if (string.IsNullOrEmpty(rule.Variable)) return false;
            var value = ResolvePath(input, rule.Variable, queryLanguage);

            if (rule.IsPresent.HasValue) return rule.IsPresent.Value ? value != null : value == null;
            if (rule.IsNull.HasValue) return rule.IsNull.Value ? (value == null || value.Type == JTokenType.Null) : (value != null && value.Type != JTokenType.Null);
            if (rule.IsString.HasValue) return rule.IsString.Value == (value?.Type == JTokenType.String);
            if (rule.IsNumeric.HasValue) return rule.IsNumeric.Value == (value?.Type == JTokenType.Integer || value?.Type == JTokenType.Float);
            if (rule.IsBoolean.HasValue) return rule.IsBoolean.Value == (value?.Type == JTokenType.Boolean);

            if (value == null) return false;

            if (rule.StringEquals != null) return value.ToString() == rule.StringEquals;
            if (rule.StringGreaterThan != null) return string.Compare(value.ToString(), rule.StringGreaterThan, StringComparison.Ordinal) > 0;
            if (rule.StringLessThan != null) return string.Compare(value.ToString(), rule.StringLessThan, StringComparison.Ordinal) < 0;
            if (rule.StringMatches != null) return Regex.IsMatch(value.ToString(), "^" + Regex.Escape(rule.StringMatches).Replace("\\*", ".*") + "$");

            if (rule.NumericEquals.HasValue) return value.Value<double>() == rule.NumericEquals.Value;
            if (rule.NumericGreaterThan.HasValue) return value.Value<double>() > rule.NumericGreaterThan.Value;
            if (rule.NumericGreaterThanEquals.HasValue) return value.Value<double>() >= rule.NumericGreaterThanEquals.Value;
            if (rule.NumericLessThan.HasValue) return value.Value<double>() < rule.NumericLessThan.Value;
            if (rule.NumericLessThanEquals.HasValue) return value.Value<double>() <= rule.NumericLessThanEquals.Value;

            if (rule.BooleanEquals.HasValue) return value.Value<bool>() == rule.BooleanEquals.Value;

            if (rule.TimestampEquals != null) return DateTime.Parse(value.ToString()) == DateTime.Parse(rule.TimestampEquals);
            if (rule.TimestampGreaterThan != null) return DateTime.Parse(value.ToString()) > DateTime.Parse(rule.TimestampGreaterThan);
            if (rule.TimestampLessThan != null) return DateTime.Parse(value.ToString()) < DateTime.Parse(rule.TimestampLessThan);

            if (rule.StringEqualsPath != null) return value.ToString() == ResolvePath(input, rule.StringEqualsPath, queryLanguage)?.ToString();

            return false;
        }

        /// <summary>JSONata-style truthiness for choice expressions: booleans as-is, numbers non-zero, strings parsed when boolean-like else non-empty, objects/arrays by value presence.</summary>
        private static bool IsTruthy(JToken? token)
        {
            if (token == null || token.Type is JTokenType.Null or JTokenType.Undefined) return false;
            switch (token.Type)
            {
                case JTokenType.Boolean: return token.Value<bool>();
                case JTokenType.Integer or JTokenType.Float: return token.Value<double>() != 0d;
                case JTokenType.String:
                    var s = token.Value<string>();
                    return bool.TryParse(s, out var parsed) ? parsed : !string.IsNullOrEmpty(s);
                default: return token.HasValues;
            }
        }

        private async Task<JToken> ExecuteWaitState(StateDefinition state, JToken input, string queryLanguage, CancellationToken ct)
        {
            TimeSpan delay = TimeSpan.Zero;
            if (state.Seconds.HasValue) delay = TimeSpan.FromSeconds(state.Seconds.Value);
            else if (!string.IsNullOrEmpty(state.Timestamp)) delay = DateTime.Parse(state.Timestamp) - DateTime.UtcNow;
            else if (!string.IsNullOrEmpty(state.SecondsPath)) delay = TimeSpan.FromSeconds(ResolvePath(input, state.SecondsPath, queryLanguage)?.Value<int>() ?? 0);
            else if (!string.IsNullOrEmpty(state.TimestampPath)) delay = DateTime.Parse(ResolvePath(input, state.TimestampPath, queryLanguage)?.ToString() ?? DateTime.UtcNow.ToString("O")) - DateTime.UtcNow;

            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            await Task.Delay(delay, ct);
            return input;
        }

        private JToken ExecuteSucceedState(StateDefinition state, JToken input, string queryLanguage, Execution execution)
        {
            execution.Status = ExecutionStatus.Succeeded;
            execution.Output = ApplyOutputPath(state, input, queryLanguage);
            return execution.Output;
        }

        private JToken ExecuteFailState(StateDefinition state, Execution execution)
        {
            execution.Status = ExecutionStatus.Failed;
            execution.ErrorCode = state.Error ?? "States.TaskFailed";
            execution.ErrorMessage = state.Cause ?? "Task failed";
            throw new StepEngineException(execution.ErrorCode, execution.ErrorMessage);
        }

        private async Task<JToken> ExecuteParallelState(StateDefinition state, JToken input, Execution execution, string queryLanguage, CancellationToken ct)
        {
            if (state.Branches == null || state.Branches.Count == 0) throw new StepEngineException("States.Runtime", "Parallel state has no branches");
            var effectiveInput = ApplyInputPath(state, input, queryLanguage);
            effectiveInput = ApplyParameters(state, effectiveInput, input, execution, queryLanguage);

            var tasks = state.Branches.Select(branch => ExecuteAsync(branch, effectiveInput.DeepClone(), $"{execution.ExecutionId}-b{Guid.NewGuid():N}"[..8], execution.StateMachineId, ct));
            var results = await Task.WhenAll(tasks);

            var failed = results.FirstOrDefault(r => r.Status == ExecutionStatus.Failed);
            if (failed != null) throw new StepEngineException(failed.ErrorCode ?? "States.BranchFailed", failed.ErrorMessage ?? "A parallel branch failed");

            var result = ApplyResultSelector(state, new JArray(results.Select(r => r.Output ?? JValue.CreateNull())), queryLanguage);
            var output = ApplyResultPath(state, input, result, queryLanguage);
            return ApplyOutputPath(state, output, queryLanguage);
        }

        private async Task<JToken> ExecuteMapState(StateDefinition state, JToken input, Execution execution, string queryLanguage, CancellationToken ct)
        {
            var effectiveInput = ApplyInputPath(state, input, queryLanguage);
            var items = ResolvePath(effectiveInput, state.ItemsPath ?? "$", queryLanguage) as JArray ?? throw new StepEngineException("States.Runtime", "ItemsPath did not resolve to array");
            var iterator = state.Iterator ?? throw new StepEngineException("States.Runtime", "Map state has no Iterator");

            var tasks = items.Select(async (item, index) =>
            {
                var iterExec = await ExecuteAsync(iterator, item.DeepClone(), $"{execution.ExecutionId}-m{index}", execution.StateMachineId, ct);
                return iterExec.Output ?? JValue.CreateNull();
            });

            var result = ApplyResultSelector(state, new JArray(await Task.WhenAll(tasks)), queryLanguage);
            var output = ApplyResultPath(state, input, result, queryLanguage);
            return ApplyOutputPath(state, output, queryLanguage);
        }

        private JToken ApplyInputPath(StateDefinition state, JToken input, string queryLanguage) => (state.InputPath == null || state.InputPath == "$") ? input : (ResolvePath(input, state.InputPath, queryLanguage) ?? JValue.CreateNull());
        private JToken ApplyParameters(StateDefinition state, JToken effectiveInput, JToken rawInput, Execution execution, string queryLanguage) => state.Parameters == null ? effectiveInput : ProcessPayloadTemplate(state.Parameters, effectiveInput, execution, queryLanguage);
        private JToken ApplyResultSelector(StateDefinition state, JToken result, string queryLanguage) => state.ResultSelector == null ? result : ProcessPayloadTemplate(state.ResultSelector, result, null, queryLanguage);
        private JToken ApplyResultPath(StateDefinition state, JToken input, JToken result, string queryLanguage)
        {
            if (state.ResultPath == null) return result;
            if (state.ResultPath == "$") return result;
            var output = input.DeepClone();
            SetPath(output, state.ResultPath, result, queryLanguage);
            return output;
        }
        private JToken ApplyOutputPath(StateDefinition state, JToken output, string queryLanguage) => (state.OutputPath == null || state.OutputPath == "$") ? output : (ResolvePath(output, state.OutputPath, queryLanguage) ?? JValue.CreateNull());

        private JToken ProcessPayloadTemplate(JToken template, JToken input, Execution? execution, string queryLanguage)
        {
            if (queryLanguage == "JSONata")
            {
                return JsonataProcessor.EvaluatePayloadTemplate(template, input);
            }

            if (template is JObject obj)
            {
                var result = new JObject();
                foreach (var prop in obj.Properties())
                {
                    if (prop.Name.EndsWith(".$"))
                    {
                        var realName = prop.Name[..^2];
                        var path = prop.Value.ToString();
                        if (path.StartsWith("$$")) result[realName] = ResolvePath(BuildContextObject(execution), "$" + path[2..], queryLanguage) ?? JValue.CreateNull();
                        else if (path.StartsWith("$")) result[realName] = ResolvePath(input, path, queryLanguage) ?? JValue.CreateNull();
                        else result[realName] = EvaluateIntrinsic(path, input);
                    }
                    else result[prop.Name] = ProcessPayloadTemplate(prop.Value, input, execution, queryLanguage);
                }
                return result;
            }
            if (template is JArray arr) return new JArray(arr.Select(item => ProcessPayloadTemplate(item, input, execution, queryLanguage)));
            return template.DeepClone();
        }

        private JToken? ResolvePath(JToken root, string path, string queryLanguage)
        {
            if (queryLanguage == "JSONata")
            {
                return JsonataProcessor.Evaluate(path, root);
            }

            if (string.IsNullOrEmpty(path) || path == "$") return root;
            try
            {
                var result = root.SelectToken(path);
                _logger.LogDebug("ResolvePath('{Path}') -> {Result}", path, result?.ToString() ?? "null");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ResolvePath('{Path}') failed", path);
                return null;
            }
        }

        private void SetPath(JToken root, string path, JToken value, string queryLanguage) => JsonPaths.Set(root, path, value);

        private JToken EvaluateIntrinsic(string expression, JToken input)
        {
            if (expression.StartsWith("States.Format("))
            {
                var content = expression["States.Format(".Length..^1].Trim();
                var parts = SplitArguments(content);
                if (parts.Count < 1) return JValue.CreateString(expression);

                var format = parts[0].Trim('\'');
                var args = parts.Skip(1).Select(p => ResolvePath(input, p, "JSONPath")?.ToString() ?? "").ToArray();
                
                try { return JValue.CreateString(string.Format(format.Replace("{}", "{0}"), args)); }
                catch { return JValue.CreateString(format); }
            }
            return JValue.CreateString(expression);
        }

        private List<string> SplitArguments(string content)
        {
            var results = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            var bracketLevel = 0;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '\'' && (i == 0 || content[i - 1] != '\\')) inQuotes = !inQuotes;
                else if (!inQuotes && c == '(') bracketLevel++;
                else if (!inQuotes && c == ')') bracketLevel--;
                else if (!inQuotes && c == ',' && bracketLevel == 0)
                {
                    results.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            results.Add(current.ToString().Trim());
            return results;
        }

        private JToken HandleCatch(StateDefinition state, JToken input, StepEngineException ex, Execution execution, out string? nextState)
        {
            nextState = null;
            if (state.Catch == null) throw ex;
            foreach (var catcher in state.Catch)
            {
                if (catcher.ErrorEquals.Contains("States.ALL") || catcher.ErrorEquals.Contains(ex.ErrorCode))
                {
                    var err = new JObject { ["Error"] = ex.ErrorCode, ["Cause"] = ex.Message };
                    nextState = catcher.Next;
                    AddEvent(execution, "TaskCaught", execution.CurrentState!, new JObject { ["error"] = ex.ErrorCode, ["next"] = catcher.Next });
                    if (catcher.ResultPath != null) { var o = input.DeepClone(); SetPath(o, catcher.ResultPath, err, "JSONPath"); return o; }
                    return err;
                }
            }
            throw ex;
        }

        private int GetMaxRetries(List<RetryRule> rules, Exception ex) => rules.FirstOrDefault(r => r.ErrorEquals.Contains("States.ALL") || r.ErrorEquals.Contains((ex as StepEngineException)?.ErrorCode ?? "States.TaskFailed"))?.MaxAttempts ?? 0;
        private int GetRetryDelay(List<RetryRule> rules, Exception ex, int att)
        {
            var r = rules.FirstOrDefault(r => r.ErrorEquals.Contains("States.ALL") || r.ErrorEquals.Contains((ex as StepEngineException)?.ErrorCode ?? "States.TaskFailed"));
            return r == null ? 1000 : (int)(r.IntervalSeconds * Math.Pow(r.BackoffRate, att) * 1000);
        }

        private JObject BuildContextObject(Execution? execution) => new JObject { ["Execution"] = new JObject { ["Id"] = execution?.ExecutionId, ["StartTime"] = execution?.StartedAt.ToString("O") }, ["State"] = new JObject { ["Name"] = execution?.CurrentState, ["EnteredTime"] = DateTime.UtcNow.ToString("O") }, ["StateMachine"] = new JObject { ["Id"] = execution?.StateMachineId } };

        private void AddEvent(Execution execution, string eventType, string stateName, JToken? detail = null, double? durationMs = null) => execution.History.Add(new HistoryEvent { Type = eventType, State = stateName, Timestamp = DateTime.UtcNow, Data = detail });

        private JToken Truncate(JToken token, int max = 500) { var s = token.ToString(Formatting.None); return s.Length <= max ? token : JValue.CreateString(s[..max] + "..."); }
    }
}
