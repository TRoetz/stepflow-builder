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
        
        public JToken Input { get; set; } = new JObject();
        public JToken Output { get; set; } = new JObject();
        
        [JsonConverter(typeof(StringEnumConverter))]
        public ExecutionStatus Status { get; set; } = ExecutionStatus.Running;
        
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        
        public string? CurrentState { get; set; }
        
        public List<HistoryEvent> History { get; set; } = new();
        public Dictionary<string, JToken> Variables { get; set; } = new();
    }

    public enum ExecutionStatus
    {
        Running,
        Succeeded,
        Failed,
        Aborted,
        TimedOut
    }

    public class HistoryEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Type { get; set; } = "";
        public string? State { get; set; }
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
}
