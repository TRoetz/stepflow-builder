using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace StepFunctionsApp.Mcp
{
    /// <summary>
    /// Shared MCP wire format: camelCase properties, no nulls, compact — definitions round-trip
    /// with the React UI (importFlow) and stay small for LLM context windows. Dictionary keys
    /// (state names, attribute names) keep their original casing; the stock camelCase resolver
    /// would lowercase them.
    /// </summary>
    internal static class McpJson
    {
        public static readonly JsonSerializerSettings Settings = new()
        {
            ContractResolver = new KeyPreservingCamelCaseResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        /// <summary>camelCase property names, but leaves dictionary keys (state names) untouched.</summary>
        internal sealed class KeyPreservingCamelCaseResolver : CamelCasePropertyNamesContractResolver
        {
            protected override string ResolveDictionaryKey(string key) => key;
        }
    }
}
