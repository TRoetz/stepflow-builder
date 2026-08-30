using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // STEP FUNCTION SERVICE
    // ═══════════════════════════════════════════════════════════════════════════════

    public class StepFunctionService : BackgroundService
    {
        private readonly ConcurrentDictionary<string, StoredStateMachine> _stateMachines = new();
        private readonly ConcurrentDictionary<string, Execution> _executions = new();
        private readonly ConcurrentQueue<PendingExecution> _pendingQueue = new();
        private readonly StepFunctionInterpreter _interpreter;
        private readonly BpmnConverter _bpmnConverter;
        private readonly ILogger<StepFunctionService> _logger;
        private readonly IFlowStateStore? _stateStore;
        private readonly FlowStateOptions _flowStateOptions = new();
        private readonly IHumanTaskStore? _humanTaskStore;

        public StepFunctionService(
            StepFunctionInterpreter interpreter,
            BpmnConverter bpmnConverter,
            ILogger<StepFunctionService> logger,
            IFlowStateStore? stateStore = null,
            IOptions<FlowStateOptions>? flowStateOptions = null,
            IHumanTaskStore? humanTaskStore = null)
        {
            _interpreter = interpreter;
            _bpmnConverter = bpmnConverter;
            _logger = logger;
            _stateStore = stateStore;
            if (flowStateOptions != null) _flowStateOptions = flowStateOptions.Value;
            _humanTaskStore = humanTaskStore;
        }

        public StoredStateMachine RegisterStateMachine(string name, StateMachineDefinition definition, string? description = null, string? id = null)
        {
            if (!string.IsNullOrEmpty(id))
            {
                // Re-registering a known id (recovery re-registration): replace the definition in place.
                var existing = _stateMachines.TryGetValue(id, out var found) ? found : new StoredStateMachine { Id = id };
                existing.Name = name;
                existing.Description = description ?? existing.Description;
                existing.Definition = definition;
                existing.UpdatedAt = DateTime.UtcNow;
                _stateMachines[id] = existing;
                _stateMachines[name] = existing; // keep the name alias pointing at it
                return existing;
            }
            if (_stateMachines.TryGetValue(name, out var byName))
            {
                byName.Definition = definition;
                byName.Description = description ?? byName.Description;
                byName.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Updated existing state machine: {Name} (Id: {Id})", name, byName.Id);
                return byName;
            }

            var sm = new StoredStateMachine
            {
                Name = name,
                Description = description,
                Definition = definition
            };
            _stateMachines[sm.Id] = sm;
            // Also register by name for easy lookup
            _stateMachines[name] = sm;
            _logger.LogInformation("Registered new state machine: {Name} (Id: {Id})", name, sm.Id);
            return sm;
        }

        public StoredStateMachine? GetStateMachine(string idOrName) =>
            _stateMachines.TryGetValue(idOrName, out var sm) ? sm : null;

        public List<StoredStateMachine> ListStateMachines() =>
            _stateMachines.Values.DistinctBy(s => s.Id).ToList();

        public Execution StartExecution(string idOrName, JToken? input)
        {
            var sm = GetStateMachine(idOrName) ?? throw new ArgumentException($"State machine '{idOrName}' not found");
            var execution = new Execution
            {
                StateMachineId = sm.Id,
                StateMachineName = sm.Name,
                Input = input ?? new JObject(),
                Status = ExecutionStatus.Running,
                CurrentState = sm.Definition.StartAt
            };
            _executions[execution.ExecutionId] = execution;
            _pendingQueue.Enqueue(new PendingExecution(sm.Definition, execution, Resume: false));
            return execution;
        }

        public async Task<Execution> ExecuteSyncAsync(string idOrName, JToken? input, CancellationToken ct = default)
        {
            var sm = GetStateMachine(idOrName) ?? throw new ArgumentException($"State machine '{idOrName}' not found");
            var executionId = Guid.NewGuid().ToString("N")[..8];
            var execution = await _interpreter.ExecuteAsync(sm.Definition, input ?? new JObject(), executionId, sm.Id, ct, SaveCheckpointAsync);
            _executions[execution.ExecutionId] = execution;
            return execution;
        }

        public Execution? GetExecution(string id) => _executions.TryGetValue(id, out var e) ? e : null;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Durable recovery: pick up executions that were running when the process died.
            try
            {
                await RecoverFromStoreAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flow-state recovery failed; continuing without recovered executions");
            }
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_pendingQueue.TryDequeue(out var pending))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var result = pending.Resume
                                ? await _interpreter.ResumeAsync(pending.Definition, pending.Execution, stoppingToken, SaveCheckpointAsync)
                                : await _interpreter.ExecuteAsync(pending.Definition, pending.Execution.Input, pending.Execution.ExecutionId, pending.Execution.StateMachineId, stoppingToken, SaveCheckpointAsync);
                            var tracked = _executions[pending.Execution.ExecutionId];
                            tracked.Status = result.Status;
                            tracked.Output = result.Output;
                            tracked.CompletedAt = result.CompletedAt;
                            tracked.ErrorCode = result.ErrorCode;
                            tracked.ErrorMessage = result.ErrorMessage;
                            tracked.History = result.History;
                            tracked.CurrentState = result.CurrentState;
                            tracked.PendingInput = result.PendingInput;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Background execution failed");
                        }
                    }, stoppingToken);
                }
                else await Task.Delay(100, stoppingToken);
            }
        }

        public void ReloadFromDirectory(string directory)
        {
            if (!Directory.Exists(directory)) return;

            // Load JSON state machines
            foreach (var file in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var definition = JsonConvert.DeserializeObject<StateMachineDefinition>(json);
                    if (definition != null)
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        RegisterStateMachine(name, definition);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load workflow from {File}", file);
                }
            }

            // Load BPMN XML files and convert to state machines
            foreach (var file in Directory.GetFiles(directory, "*.bpmn"))
            {
                try
                {
                    var xml = File.ReadAllText(file);
                    var definition = _bpmnConverter.ConvertXml(xml);
                    var name = Path.GetFileNameWithoutExtension(file);
                    RegisterStateMachine(name, definition, $"Imported from BPMN: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to convert BPMN file {File}", file);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // DURABLE STATE: checkpoints, recovery, resume
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>Checkpoint callback handed to the interpreter. Never throws — store failures are logged.</summary>
        private async Task SaveCheckpointAsync(Execution execution)
        {
            if (_stateStore == null || string.IsNullOrEmpty(execution.StateMachineId)) return;
            var sm = GetStateMachine(execution.StateMachineId);
            try
            {
                await _stateStore.SaveAsync(new FlowStateRecord
                {
                    ExecutionId = execution.ExecutionId,
                    Definition = sm?.Definition ?? new StateMachineDefinition(),
                    Execution = execution
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Checkpoint save failed for execution {ExecutionId} ({Provider})", execution.ExecutionId, _stateStore.ProviderName);
            }
        }

        /// <summary>
        /// Startup recovery: re-register state machines found in the store and resume (or suspend)
        /// executions that were Running when the process died. Called from ExecuteAsync before the loop.
        /// </summary>
        public async Task RecoverFromStoreAsync(CancellationToken ct = default)
        {
            if (_stateStore == null) return;

            IReadOnlyList<FlowStateSummary> summaries;
            try
            {
                summaries = await _stateStore.ListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flow-state store listing failed ({Provider}); skipping recovery", _stateStore.ProviderName);
                return;
            }

            foreach (var summary in summaries)
            {
                ct.ThrowIfCancellationRequested();
                FlowStateRecord? record;
                try
                {
                    record = await _stateStore.LoadAsync(summary.ExecutionId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load checkpoint for execution {ExecutionId}", summary.ExecutionId);
                    continue;
                }
                if (record?.Definition == null || record.Definition.States.Count == 0) continue;

                var status = record.Execution.Status;
                if (status is ExecutionStatus.Succeeded or ExecutionStatus.Failed or ExecutionStatus.TimedOut or ExecutionStatus.Aborted)
                    continue; // terminal: keep the record for history only - never clobber live definitions with stale ones

                // Re-register the embedded definition so lookups by StateMachineId work again.
                RegisterStateMachine(record.Execution.StateMachineName, record.Definition, id: record.Execution.StateMachineId);

                _executions[record.Execution.ExecutionId] = record.Execution;

                if (!_flowStateOptions.AutoResume || status == ExecutionStatus.Suspended)
                {
                    record.Execution.Status = ExecutionStatus.Suspended;
                    await SaveCheckpointAsync(record.Execution);
                    _logger.LogInformation("Execution {ExecutionId} ({Name}) left Suspended (AutoResume={AutoResume})", record.Execution.ExecutionId, record.Execution.StateMachineName, _flowStateOptions.AutoResume);
                    continue;
                }

                _pendingQueue.Enqueue(new PendingExecution(record.Definition, record.Execution, Resume: true));
                _logger.LogInformation("Resuming execution {ExecutionId} ({Name}) from state {State}", record.Execution.ExecutionId, record.Execution.StateMachineName, record.Execution.CurrentState);
            }
        }

        /// <summary>Manually resume a Suspended (or previously failed) stored execution.</summary>
        public async Task<Execution> ResumeStoredExecutionAsync(string executionId, CancellationToken ct = default)
        {
            if (_stateStore == null) throw new InvalidOperationException("No flow-state store configured");
            var record = await _stateStore.LoadAsync(executionId, ct);
            if (record?.Definition == null || record.Definition.States.Count == 0)
                throw new ArgumentException($"Execution '{executionId}' not found in the flow-state store");

            RegisterStateMachine(record.Execution.StateMachineName, record.Definition, id: record.Execution.StateMachineId);

            var execution = _executions.GetOrAdd(executionId, record.Execution);
            if (execution.Status == ExecutionStatus.Running)
                throw new InvalidOperationException($"Execution '{executionId}' is already running");

            execution.Status = ExecutionStatus.Running; // matches StartExecution: queued work counts as Running
            _pendingQueue.Enqueue(new PendingExecution(record.Definition, execution, Resume: true));
            return execution;
        }

        /// <summary>All stored executions (any status), oldest checkpoint first.</summary>
        public async Task<IReadOnlyList<FlowStateSummary>> ListStoredExecutionsAsync(CancellationToken ct = default) =>
            _stateStore == null ? Array.Empty<FlowStateSummary>() : await _stateStore.ListAsync(ct);

        /// <summary>Drop a stored execution's checkpoint (e.g. after the user discards it).</summary>
        public async Task DeleteStoredExecutionAsync(string executionId, CancellationToken ct = default)
        {
            if (_stateStore == null) return;
            await _stateStore.DeleteAsync(executionId, ct);
            _executions.TryRemove(executionId, out _);
        }

        /// <summary>
        /// Completes a pending human task with an external result. Merges the result into the suspended
        /// execution's input at ResultPath, then resumes from the state after the human task — or ends
        /// the flow when the human task was terminal.
        /// </summary>
        public async Task<HumanTaskRecord?> CompleteHumanTaskAsync(string taskId, JToken? result = null, CancellationToken ct = default)
        {
            if (_humanTaskStore == null) throw new InvalidOperationException("No human task store configured");

            var record = await _humanTaskStore.LoadAsync(taskId);
            if (record == null) return null;
            if (record.Status != HumanTaskStatus.Pending)
                throw new InvalidOperationException($"Human task {taskId} is already {record.Status}");

            result ??= JValue.CreateNull();
            record.Result = result;
            record.CompletedAtUtc = DateTime.UtcNow;
            record.Status = HumanTaskStatus.Completed;
            await _humanTaskStore.SaveAsync(record);

            if (_stateStore == null) throw new InvalidOperationException("No flow-state store configured");
            var stored = await _stateStore.LoadAsync(record.ExecutionId, ct);
            if (stored?.Definition == null || stored.Definition.States.Count == 0)
                throw new InvalidOperationException($"Execution {record.ExecutionId} not found in the state store");

            var execution = stored.Execution;
            if (execution.Status != ExecutionStatus.Suspended)
                throw new InvalidOperationException($"Execution {record.ExecutionId} is {execution.Status}, expected Suspended");

            // Merge the completion result into the suspended input at ResultPath.
            var baseInput = execution.PendingInput ?? new JObject();
            JToken merged;
            if (string.IsNullOrEmpty(record.ResultPath))
            {
                merged = result.DeepClone();
            }
            else
            {
                merged = baseInput.DeepClone();
                JsonPaths.Set(merged, record.ResultPath, result.DeepClone());
            }

            execution.History.Add(new HistoryEvent
            {
                Type = "HumanTaskCompleted",
                State = record.StateName,
                Data = new JObject { ["taskId"] = taskId, ["result"] = result }
            });

            if (record.IsEnd)
            {
                execution.Status = ExecutionStatus.Succeeded;
                execution.Output = merged;
                execution.CompletedAt = DateTime.UtcNow;
                await _stateStore.SaveAsync(new FlowStateRecord { ExecutionId = record.ExecutionId, Definition = stored.Definition, Execution = execution }, ct);
                // Reflect the terminal state in memory so GetExecution agrees with the store.
                _executions[record.ExecutionId] = execution;
                return record;
            }

            if (string.IsNullOrEmpty(record.NextState))
                throw new InvalidOperationException($"Human task {taskId} has no next state and is not terminal");

            execution.Status = ExecutionStatus.Running;
            execution.CurrentState = record.NextState;
            execution.PendingInput = merged;
            await _stateStore.SaveAsync(new FlowStateRecord { ExecutionId = record.ExecutionId, Definition = stored.Definition, Execution = execution }, ct);

            // Replace the stale suspended instance so the queue worker can track the resumed execution.
            _executions[record.ExecutionId] = execution;
            _pendingQueue.Enqueue(new PendingExecution(stored.Definition, execution, Resume: true));
            return record;
        }
        /// <summary>Load a stored execution's latest checkpoint, or null when absent (or no store configured).</summary>
        public Task<FlowStateRecord?> LoadStoredExecutionAsync(string executionId, CancellationToken ct = default) =>
            _stateStore == null ? Task.FromResult<FlowStateRecord?>(null) : _stateStore.LoadAsync(executionId, ct);

        /// <summary>Number of executions currently Running in memory (for the health endpoint).</summary>
        public int RunningExecutionCount => _executions.Values.Count(e => e.Status == ExecutionStatus.Running);
        private record PendingExecution(StateMachineDefinition Definition, Execution Execution, bool Resume);
    }
}
