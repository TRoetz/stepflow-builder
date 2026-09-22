using System.Collections.Generic;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // EAV MODELS — entity contract definitions (registry + attribute-domain bridge)
    // ═══════════════════════════════════════════════════════════════════════════════

    public class EavEntityDefinition
    {
        public string EntityName { get; set; } = "";
        public string? Description { get; set; }
        public List<EavAttributeDefinition> Attributes { get; set; } = new();
    }

    public class EavAttributeDefinition
    {
        public string AttributeName { get; set; } = "";
        public string DataType { get; set; } = "string"; // string, number, boolean, date
        public bool IsRequired { get; set; } = false;
        public object? DefaultValue { get; set; }

        // The magic: A JSONPath expression to extract this value from the dynamic workflow payload
        public string JsonPathMapping { get; set; } = "";
    }
}
