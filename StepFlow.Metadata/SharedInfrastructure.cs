using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// Shared infrastructure types used across StepFlow component packages (kept in the lowest-level
// library so EAV, ASL and DataExchange can all reference them without cycles).

/// <summary>camelCase property names, but leaves dictionary keys (state names) untouched. Public so non-MVC writers (e.g. DynamicApiDispatcher, EavQuery) emit the same wire shape.</summary>
public sealed class KeyPreservingCamelCaseContractResolver : CamelCasePropertyNamesContractResolver
{
    protected override string ResolveDictionaryKey(string key) => key;
}

namespace StepFunctionsApp.StepFunctions
{
    /// <summary>Engine failure with a stable error code (thrown by ASL interpreter, EAV mapping and resource handlers).</summary>
    public class StepEngineException : Exception
    {
        public string ErrorCode { get; }
        public StepEngineException(string errorCode, string message) : base(message) => ErrorCode = errorCode;
    }
}
