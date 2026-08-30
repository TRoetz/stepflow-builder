using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // AMAZON STATES LANGUAGE MODEL
    // ═══════════════════════════════════════════════════════════════════════════════

    public class StateMachineDefinition
    {
        [JsonProperty("Comment")]
        public string? Comment { get; set; }

        [JsonProperty("StartAt")]
        public string StartAt { get; set; } = "";

        [JsonProperty("States")]
        public Dictionary<string, StateDefinition> States { get; set; } = new();

        [JsonProperty("Version")]
        public string Version { get; set; } = "1.0";

        [JsonProperty("TimeoutSeconds")]
        public int? TimeoutSeconds { get; set; }

        [JsonProperty("QueryLanguage")]
        public string QueryLanguage { get; set; } = "JSONPath";
    }

    public class StateDefinition
    {
        [JsonProperty("Type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public StateType Type { get; set; }

        [JsonProperty("Comment")]
        public string? Comment { get; set; }

        [JsonProperty("Next")]
        public string? Next { get; set; }

        [JsonProperty("End")]
        public bool? End { get; set; }

        [JsonProperty("Resource")]
        public string? Resource { get; set; }

        [JsonProperty("TimeoutSeconds")]
        public int? TimeoutSeconds { get; set; }

        [JsonProperty("HeartbeatSeconds")]
        public int? HeartbeatSeconds { get; set; }

        [JsonProperty("Result")]
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken? Result { get; set; }

        [JsonProperty("Seconds")]
        public int? Seconds { get; set; }

        [JsonProperty("Timestamp")]
        public string? Timestamp { get; set; }

        [JsonProperty("SecondsPath")]
        public string? SecondsPath { get; set; }

        [JsonProperty("TimestampPath")]
        public string? TimestampPath { get; set; }

        [JsonProperty("Error")]
        public string? Error { get; set; }

        [JsonProperty("Cause")]
        public string? Cause { get; set; }

        [JsonProperty("Choices")]
        public List<ChoiceRule>? Choices { get; set; }

        [JsonProperty("Default")]
        public string? Default { get; set; }

        [JsonProperty("Branches")]
        public List<StateMachineDefinition>? Branches { get; set; }

        [JsonProperty("ItemsPath")]
        public string? ItemsPath { get; set; }

        [JsonProperty("Iterator")]
        public StateMachineDefinition? Iterator { get; set; }

        [JsonProperty("MaxConcurrency")]
        public int? MaxConcurrency { get; set; }

        [JsonProperty("InputPath")]
        public string? InputPath { get; set; }

        [JsonProperty("OutputPath")]
        public string? OutputPath { get; set; }

        [JsonProperty("ResultPath")]
        public string? ResultPath { get; set; }

        [JsonProperty("Parameters")]
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken? Parameters { get; set; }

        [JsonProperty("ResultSelector")]
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken? ResultSelector { get; set; }

        [JsonProperty("Task")]
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken? Task { get; set; }

        [JsonProperty("Completion")]
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken? Completion { get; set; }

        [JsonProperty("Retry")]
        public List<RetryRule>? Retry { get; set; }

        [JsonProperty("Catch")]
        public List<CatchRule>? Catch { get; set; }
    }

    public enum StateType
    {
        Task,
        Pass,
        Choice,
        Wait,
        HumanTask,
        /// <summary>Suspends until a JSON-configured form is filled and submitted via the API.</summary>
        FormCapture,
        Succeed,
        Fail,
        Parallel,
        Map
    }

    public class ChoiceRule
    {
        [JsonProperty("Variable")]
        public string? Variable { get; set; }

        [JsonProperty("Next")]
        public string? Next { get; set; }

        [JsonProperty("StringEquals")]
        public string? StringEquals { get; set; }

        [JsonProperty("StringEqualsPath")]
        public string? StringEqualsPath { get; set; }

        [JsonProperty("StringGreaterThan")]
        public string? StringGreaterThan { get; set; }

        [JsonProperty("StringLessThan")]
        public string? StringLessThan { get; set; }

        [JsonProperty("NumericEquals")]
        public double? NumericEquals { get; set; }

        [JsonProperty("NumericGreaterThan")]
        public double? NumericGreaterThan { get; set; }

        [JsonProperty("NumericGreaterThanEquals")]
        public double? NumericGreaterThanEquals { get; set; }

        [JsonProperty("NumericLessThan")]
        public double? NumericLessThan { get; set; }

        [JsonProperty("NumericLessThanEquals")]
        public double? NumericLessThanEquals { get; set; }

        [JsonProperty("BooleanEquals")]
        public bool? BooleanEquals { get; set; }

        [JsonProperty("TimestampEquals")]
        public string? TimestampEquals { get; set; }

        [JsonProperty("TimestampGreaterThan")]
        public string? TimestampGreaterThan { get; set; }

        [JsonProperty("TimestampLessThan")]
        public string? TimestampLessThan { get; set; }

        [JsonProperty("IsPresent")]
        public bool? IsPresent { get; set; }

        [JsonProperty("IsNull")]
        public bool? IsNull { get; set; }

        [JsonProperty("IsString")]
        public bool? IsString { get; set; }

        [JsonProperty("IsNumeric")]
        public bool? IsNumeric { get; set; }

        [JsonProperty("IsBoolean")]
        public bool? IsBoolean { get; set; }

        [JsonProperty("StringMatches")]
        public string? StringMatches { get; set; }

        [JsonProperty("And")]
        public List<ChoiceRule>? And { get; set; }

        [JsonProperty("Or")]
        public List<ChoiceRule>? Or { get; set; }

        [JsonProperty("Not")]
        public ChoiceRule? Not { get; set; }
    }

    public class RetryRule
    {
        [JsonProperty("ErrorEquals")]
        public List<string> ErrorEquals { get; set; } = new();

        [JsonProperty("IntervalSeconds")]
        public int IntervalSeconds { get; set; } = 1;

        [JsonProperty("MaxAttempts")]
        public int MaxAttempts { get; set; } = 3;

        [JsonProperty("BackoffRate")]
        public double BackoffRate { get; set; } = 2.0;
    }

    public class CatchRule
    {
        [JsonProperty("ErrorEquals")]
        public List<string> ErrorEquals { get; set; } = new();

        [JsonProperty("Next")]
        public string Next { get; set; } = "";

        [JsonProperty("ResultPath")]
        public string? ResultPath { get; set; }
    }
}
