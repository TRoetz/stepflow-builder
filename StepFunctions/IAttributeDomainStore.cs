using StepFlow.DataModel.Entities.MetaData;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // ATTRIBUTE DOMAIN STORE (MetaData DataInterface)
    // Persists AttributeDomain + EntityAttribute contracts (the attribute side of the
    // dynamic-form system). Binding is by name at runtime; int FKs on the POCOs stay unused.
    // ═══════════════════════════════════════════════════════════════════════════════

    public interface IAttributeDomainStore
    {
        /// <summary>"json" | "sqlite"</summary>
        string ProviderName { get; }
        /// <summary>All domains with attributes loaded; linked domains carry a SchemaDefinition stub (name + version).</summary>
        IReadOnlyList<AttributeDomain> GetAll();
        /// <summary>Looks up a domain by name (case-insensitive) with its optional linked SchemaDefinition.</summary>
        (AttributeDomain? domain, SchemaDefinition? schema) GetByName(string domainName);
        /// <summary>Upserts by AttributeDomainName; replaces the attribute set wholesale. Optionally links an existing SchemaDefinition by name + version.</summary>
        void Save(AttributeDomain domain, SchemaDefinition? schema = null);
        bool Delete(string domainName);
    }
}
