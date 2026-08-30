namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SCHEMA DEFINITION STORE — versioned registry of schema definitions (MetaData
    // SchemaDefinition POCO, same type IAttributeDomainStore already exposes). One
    // entry per (SchemaDefinitionName, Version); the JSON body lives in Definition.
    // Attribute domains pin a specific (name, version) pair; Get(name) without a
    // version resolves the latest saved version (legacy domain rows that predate
    // explicit version links fall back to it).
    // ═══════════════════════════════════════════════════════════════════════════════

    public interface ISchemaDefinitionStore
    {
        /// <summary>"json" | "sqlite"</summary>
        string ProviderName { get; }
        /// <summary>All saved versions of every schema (the UI groups by name).</summary>
        IReadOnlyList<SchemaDefinition> GetAll();
        /// <summary>The latest version of a schema, or null when the schema does not exist.</summary>
        SchemaDefinition? Get(string name);
        /// <summary>An exact (name, version) pair, or null when that version was never saved.</summary>
        SchemaDefinition? Get(string name, string version);
        /// <summary>Upserts by (SchemaDefinitionName, Version). Validates non-empty name/version and that a non-empty Definition parses as JSON.</summary>
        void Save(SchemaDefinition def);
        /// <summary>Deletes one saved version; false when it does not exist.</summary>
        bool Delete(string name, string version);
    }
}
