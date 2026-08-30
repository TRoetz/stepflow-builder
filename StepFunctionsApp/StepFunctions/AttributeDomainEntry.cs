using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.DataExchange;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // ATTRIBUTE DOMAIN ENTRY — persistence DTOs for the domain registry.
    // The MetaData POCOs carry EF navigation properties (back-references, value-data
    // collections) that would recurse or bloat file/DB payloads; stores and controllers
    // therefore round-trip through these flat shapes instead. JSON shape (camelCase):
    //   [ { "schemaDefinition": {...}, "attributeDomain": { ..., "attributes": [...] } } ]
    // ═══════════════════════════════════════════════════════════════════════════════

    public sealed class AttributeDomainEntry
    {
        /// <summary>Optional linked schema definition (null = unlinked).</summary>
        public SchemaDefinitionRef? SchemaDefinition { get; set; }
        public AttributeDomainData AttributeDomain { get; set; } = new();
    }

    public sealed class SchemaDefinitionRef
    {
        public string SchemaDefinitionName { get; set; } = "";
        public string Version { get; set; } = "1";
        public string? Description { get; set; }
    }

    public sealed class AttributeDomainData
    {
        public string Version { get; set; } = "1";
        public string AttributeDomainName { get; set; } = "";
        public string? Description { get; set; }
        public bool IsCurrentVersion { get; set; } = true;
        public List<EntityAttributeData> Attributes { get; set; } = new();
    }

    public sealed class EntityAttributeData
    {
        public string AttributeName { get; set; } = "";
        /// <summary>Serialized as the enum ordinal (String=0, Boolean=1, Number=2, Date=3, Object=4, Array=5).</summary>
        public AttributeDataType DataType { get; set; }
        public string? Description { get; set; }
        public string DisplayName { get; set; } = "";
        public string Placeholder { get; set; } = "";
        public string HelpText { get; set; } = "";
        public bool Visible { get; set; } = true;
        public bool ReadOnly { get; set; }
        public bool PrimaryKey { get; set; }
        public string? ValidationSchemaJson { get; set; }
    }

    /// <summary>Converts between the flat persistence DTOs and the MetaData POCOs.</summary>
    public static class AttributeDomainMapping
    {
        public static (AttributeDomain domain, SchemaDefinition? schema) ToPoco(AttributeDomainEntry entry)
        {
            var d = entry.AttributeDomain;
            var domain = new AttributeDomain
            {
                Version = d.Version ?? "1",
                AttributeDomainName = d.AttributeDomainName,
                Description = d.Description ?? "",
                IsCurrentVersion = d.IsCurrentVersion,
                Attributes = (d.Attributes ?? new()).Select(ToPoco).ToList()
            };

            SchemaDefinition? schema = null;
            if (entry.SchemaDefinition != null && !string.IsNullOrEmpty(entry.SchemaDefinition.SchemaDefinitionName))
            {
                var s = entry.SchemaDefinition;
                schema = new SchemaDefinition
                {
                    SchemaDefinitionName = s.SchemaDefinitionName,
                    Version = s.Version ?? "1",
                    Description = s.Description ?? "",
                    AttributeDomain = true
                };
            }

            return (domain, schema);
        }

        public static EntityAttribute ToPoco(EntityAttributeData a) => new()
        {
            AttributeName = a.AttributeName,
            DataType = a.DataType,
            Description = a.Description ?? "",
            DisplayName = a.DisplayName ?? "",
            Placeholder = a.Placeholder ?? "",
            HelpText = a.HelpText ?? "",
            Visible = a.Visible,
            ReadOnly = a.ReadOnly,
            PrimaryKey = a.PrimaryKey,
            ValidationSchemaJson = a.ValidationSchemaJson ?? ""
        };

        public static AttributeDomainEntry FromPoco(AttributeDomain domain, SchemaDefinition? schema) => new()
        {
            SchemaDefinition = schema == null || string.IsNullOrEmpty(schema.SchemaDefinitionName) ? null : new SchemaDefinitionRef
            {
                SchemaDefinitionName = schema.SchemaDefinitionName,
                Version = schema.Version ?? "1",
                Description = schema.Description
            },
            AttributeDomain = new AttributeDomainData
            {
                Version = domain.Version ?? "1",
                AttributeDomainName = domain.AttributeDomainName,
                Description = string.IsNullOrEmpty(domain.Description) ? null : domain.Description,
                IsCurrentVersion = domain.IsCurrentVersion,
                Attributes = (domain.Attributes ?? new List<EntityAttribute>()).Select(FromPoco).ToList()
            }
        };

        public static EntityAttributeData FromPoco(EntityAttribute a) => new()
        {
            AttributeName = a.AttributeName,
            DataType = a.DataType,
            Description = string.IsNullOrEmpty(a.Description) ? null : a.Description,
            DisplayName = a.DisplayName ?? "",
            Placeholder = a.Placeholder ?? "",
            HelpText = a.HelpText ?? "",
            Visible = a.Visible,
            ReadOnly = a.ReadOnly,
            PrimaryKey = a.PrimaryKey,
            ValidationSchemaJson = string.IsNullOrEmpty(a.ValidationSchemaJson) ? null : a.ValidationSchemaJson
        };
    }
}
