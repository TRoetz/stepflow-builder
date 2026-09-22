using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // EAV ENTITY PROVIDER — unified lookup of rule-addressable EAV entity contracts.
    // CompositeEavEntityProvider merges the legacy eav_registry.json entities with
    // attribute domains from IAttributeDomainStore (json or sqlite provider). On a
    // name collision the domain store wins: it is the newer, builder-managed source.
    // ═══════════════════════════════════════════════════════════════════════════════

    public interface IEavEntityProvider
    {
        /// <summary>All rule-addressable entities (registry ∪ domains), ordered by name.</summary>
        IEnumerable<EavEntityDefinition> GetAllEntities();

        /// <summary>Case-insensitive lookup; null when unknown. Domain store takes precedence over the registry.</summary>
        EavEntityDefinition? GetEntity(string entityName);
    }

    public sealed class CompositeEavEntityProvider : IEavEntityProvider
    {
        private readonly EavRegistryService _registry;
        private readonly IAttributeDomainStore _domains;

        public CompositeEavEntityProvider(EavRegistryService registry, IAttributeDomainStore domains)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _domains = domains ?? throw new ArgumentNullException(nameof(domains));
        }

        public IEnumerable<EavEntityDefinition> GetAllEntities()
        {
            var merged = new Dictionary<string, EavEntityDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _registry.GetAllEntities()) merged[e.EntityName] = e;
            // Domains overwrite registry entries on collision.
            foreach (var d in _domains.GetAll().Select(FromAttributeDomain)) merged[d.EntityName] = d;
            return merged.Values.OrderBy(e => e.EntityName, StringComparer.OrdinalIgnoreCase);
        }

        public EavEntityDefinition? GetEntity(string entityName)
        {
            var domain = _domains.GetByName(entityName).domain;
            if (domain != null) return FromAttributeDomain(domain);
            return _registry.GetEntity(entityName);
        }

        /// <summary>
        /// Converts an attribute domain into a rule-addressable EAV entity. Attribute names double as the
        /// JSONPath mappings: form-captured values are merged into flow input keyed by attribute name, so
        /// rule states reading that payload resolve {AttrName} placeholders directly.
        /// </summary>
        public static EavEntityDefinition FromAttributeDomain(AttributeDomain domain) => new()
        {
            EntityName = domain.AttributeDomainName,
            Description = domain.Description,
            Attributes = (domain.Attributes ?? new List<EntityAttribute>()).Select(a => new EavAttributeDefinition
            {
                AttributeName = a.AttributeName,
                DataType = a.DataType switch
                {
                    AttributeDataType.String => "string",
                    AttributeDataType.Number => "number",
                    AttributeDataType.Boolean => "boolean",
                    AttributeDataType.Date => "date",
                    // Object/Array fall through to the mapper's default branch (no cast).
                    _ => a.DataType.ToString().ToLowerInvariant()
                },
                IsRequired = a.PrimaryKey || HasRequiredValidation(a.ValidationSchemaJson),
                JsonPathMapping = a.AttributeName
            }).ToList()
        };

        private static bool HasRequiredValidation(string? validationSchemaJson)
        {
            if (string.IsNullOrWhiteSpace(validationSchemaJson)) return false;
            try { return JToken.Parse(validationSchemaJson)["required"]?.Value<bool>() == true; }
            catch { return false; } // malformed schema JSON ⇒ treat as optional, never break rule dispatch
        }
    }
}
