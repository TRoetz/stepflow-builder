namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // FORM DATA OPTIONS — provider selection for the form-capture subsystem (FormData
    // section in appsettings.json). "json" keeps definitions file-based (forms/ +
    // attribute_domains.json); "sqlite" backs both stores with one shared database.
    // Mirrors FlowStateOptions / HumanTaskOptions conventions.
    // ═══════════════════════════════════════════════════════════════════════════════

    public sealed class FormDataOptions
    {
        public const string SectionName = "FormData";

        /// <summary>"json" | "sqlite"</summary>
        public string Provider { get; set; } = "json";

    /// <summary>SQLite database file used when Provider is "sqlite".</summary>
    public string DatabasePath { get; set; } = "stepflow_data.db";

    /// <summary>Schema-definition registry file used when Provider is "json".</summary>
    public string SchemasFile { get; set; } = "schema_definitions.json";
    }
}
