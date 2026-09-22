using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // AI DECISION MODELS
    // ═══════════════════════════════════════════════════════════════════════════════

    public class AiDecisionInput
    {
        [JsonProperty("question")]
        public string Question { get; set; } = "";

        [JsonProperty("context")]
        public object? Context { get; set; }

        [JsonProperty("provider")]
        public string? Provider { get; set; }

        [JsonProperty("systemPrompt")]
        public string? SystemPrompt { get; set; }

        [JsonProperty("confidenceThreshold")]
        public double ConfidenceThreshold { get; set; } = 0.0;
    }

    public class AiDecisionResult
    {
        [JsonProperty("decision")]
        public bool Decision { get; set; }

        [JsonProperty("answer")]
        public string Answer { get; set; } = "";

        [JsonProperty("reasoning")]
        public string Reasoning { get; set; } = "";

        [JsonProperty("confidence")]
        public double Confidence { get; set; }

        [JsonProperty("provider")]
        public string Provider { get; set; } = "";

        [JsonProperty("model")]
        public string Model { get; set; } = "";

        [JsonProperty("meetsThreshold")]
        public bool MeetsThreshold { get; set; } = true;

        [JsonProperty("durationMs")]
        public double DurationMs { get; set; }

        [JsonProperty("isError")]
        public bool IsError { get; set; }

        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // RULE ENGINE MODELS
    // ═══════════════════════════════════════════════════════════════════════════════

    public class RuleDefinition
    {
        public string RuleId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Expression { get; set; }
        public string? RuleType { get; set; }
        public string? Category { get; set; }
        public string? ComplianceLevel { get; set; }
        public bool IsBlocking { get; set; }
        public bool IsActive { get; set; } = true;
        public List<string> RequiredParameters { get; set; } = new();
    }

    public class RuleResultEx
    {
        public bool Status { get; set; }
        public bool HasErrored { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ExecutedSql { get; set; }
    }

    public class DomainInfo
    {
        public int DomainId { get; set; }
        public string DomainName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string DatasetName { get; set; } = "";
        public int RowCount { get; set; }
        public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
    }
}
