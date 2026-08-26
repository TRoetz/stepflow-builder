using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // FORM DEFINITION STORE (UIData DataInterface)
    // A FormDefinition is a JSON-configured page (UIPage per DataExchange/UIData/uidata-schema.json)
    // optionally bound to an AttributeDomain by name. Bound forms validate submissions against
    // the domain's EntityAttribute contracts and persist captured values as EAV rows.
    // ═══════════════════════════════════════════════════════════════════════════════

    public sealed class FormDefinition
    {
        public string FormId { get; set; } = "";
        /// <summary>Version label (auto-incremented integer as a string, e.g. "1", "2").</summary>
        public string Version { get; set; } = "1";
        /// <summary>True for the version flow steps resolve when they do not pin one.</summary>
        public bool IsCurrentVersion { get; set; } = true;
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        /// <summary>Name of the bound AttributeDomain entry (null/empty = unbound form).</summary>
        public string? AttributeDomainName { get; set; }
        public string? SchemaDefinitionName { get; set; }
        /// <summary>The full UIPage JSON per uidata-schema.json.</summary>
        public JToken Page { get; set; } = new JObject();
    }

    public interface IFormDefinitionStore
    {
        /// <summary>"json" | "sqlite"</summary>
        string ProviderName { get; }
        /// <summary>All saved versions of every form (the UI groups by FormId).</summary>
        IReadOnlyList<FormDefinition> GetAll();
        /// <summary>The current version of a form, or null when the form does not exist.</summary>
        FormDefinition? Get(string formId);
        /// <summary>An exact (formId, version) pair, or null when that version was never saved.</summary>
        FormDefinition? Get(string formId, string version);
        /// <summary>Upserts by (FormId, Version). When IsCurrentVersion is true the other versions' flags are cleared first. Validates non-empty FormId/Version and that Page has RootElements.</summary>
        void Save(FormDefinition def);
        /// <summary>Deletes every saved version of the form.</summary>
        bool Delete(string formId);
    }
}
