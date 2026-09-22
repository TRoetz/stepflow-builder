using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // EXECUTION MODELS
    // ═══════════════════════════════════════════════════════════════════════════════

    public class Execution
    {
        public string ExecutionId { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string StateMachineId { get; set; } = "";
        public string StateMachineName { get; set; } = "";
        
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken Input { get; set; } = new JObject();
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken Output { get; set; } = new JObject();
        
        [JsonConverter(typeof(StringEnumConverter))]
        public ExecutionStatus Status { get; set; } = ExecutionStatus.Running;
        
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        
        public string? CurrentState { get; set; }
        
        /// <summary>
        /// The input that will be fed into <see cref="CurrentState"/> on resume. Refreshed at every
        /// state boundary so a checkpoint always describes an exact resume point (at-least-once:
        /// the in-flight state may re-execute, completed states never do).
        /// </summary>
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken? PendingInput { get; set; }
        
        public List<HistoryEvent> History { get; set; } = new();
        public Dictionary<string, JToken> Variables { get; set; } = new();
    }

    public enum ExecutionStatus
    {
        Running,
        Succeeded,
        Failed,
        Aborted,
        Suspended,
        TimedOut
    }

    public class HistoryEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Type { get; set; } = "";
        public string? State { get; set; }
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken? Data { get; set; }
    }

    public class RegisterStateMachineRequest
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public StateMachineDefinition Definition { get; set; } = new();
        public Dictionary<string, string>? Tags { get; set; }
    }

    public class StoredStateMachine
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public StateMachineDefinition Definition { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // DURABLE FLOW STATE (checkpoint records persisted by IFlowStateStore)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A self-contained checkpoint of an execution: the full state plus an embedded copy of the
    /// state machine definition, so a restarted process can resume without any prior registration.
    /// Stores serialize this immediately on save; do not mutate the Execution after handing it over.
    /// </summary>
    public class FlowStateRecord
    {
        public string ExecutionId { get; set; } = "";
        public StateMachineDefinition Definition { get; set; } = new();
        public Execution Execution { get; set; } = new();
        public DateTime CheckpointedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Lightweight projection of a stored execution for list views.</summary>
    public class FlowStateSummary
    {
        public string ExecutionId { get; set; } = "";
        public string StateMachineName { get; set; } = "";
        public string Status { get; set; } = "";
        public string? CurrentState { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CheckpointedAt { get; set; }
    }
}
