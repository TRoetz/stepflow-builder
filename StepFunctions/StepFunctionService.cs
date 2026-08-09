using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
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

        public StepFunctionService(
            StepFunctionInterpreter interpreter,
            BpmnConverter bpmnConverter,
            ILogger<StepFunctionService> logger)
        {
            _interpreter = interpreter;
            _bpmnConverter = bpmnConverter;
            _logger = logger;
        }

        public StoredStateMachine RegisterStateMachine(string name, StateMachineDefinition definition, string? description = null)
        {
            if (_stateMachines.TryGetValue(name, out var existing))
            {
                existing.Definition = definition;
                existing.Description = description ?? existing.Description;
                existing.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Updated existing state machine: {Name} (Id: {Id})", name, existing.Id);
                return existing;
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
            _pendingQueue.Enqueue(new PendingExecution(sm.Definition, execution));
            return execution;
        }

        public async Task<Execution> ExecuteSyncAsync(string idOrName, JToken? input, CancellationToken ct = default)
        {
            var sm = GetStateMachine(idOrName) ?? throw new ArgumentException($"State machine '{idOrName}' not found");
            var executionId = Guid.NewGuid().ToString("N")[..8];
            var execution = await _interpreter.ExecuteAsync(sm.Definition, input ?? new JObject(), executionId, sm.Id, ct);
            _executions[execution.ExecutionId] = execution;
            return execution;
        }

        public Execution? GetExecution(string id) => _executions.TryGetValue(id, out var e) ? e : null;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_pendingQueue.TryDequeue(out var pending))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var result = await _interpreter.ExecuteAsync(pending.Definition, pending.Execution.Input, pending.Execution.ExecutionId, pending.Execution.StateMachineId, stoppingToken);
                            var tracked = _executions[pending.Execution.ExecutionId];
                            tracked.Status = result.Status;
                            tracked.Output = result.Output;
                            tracked.CompletedAt = result.CompletedAt;
                            tracked.ErrorCode = result.ErrorCode;
                            tracked.ErrorMessage = result.ErrorMessage;
                            tracked.History = result.History;
                            tracked.CurrentState = result.CurrentState;
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

        private record PendingExecution(StateMachineDefinition Definition, Execution Execution);
    }
}
