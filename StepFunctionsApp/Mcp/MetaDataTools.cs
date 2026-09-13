using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Mcp
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // MCP METADATA TOOLS — manage attribute domains (entity contracts) and versioned
    // schema definitions. Attribute domains are the "known entity" view used by rule://
    // and eav://read; schema definitions pin a (name, version) pair of JSON body.
    // ═══════════════════════════════════════════════════════════════════════════════

    [McpServerToolType]
    public class MetaDataTools
    {
        private readonly IAttributeDomainStore _domains;
        private readonly ISchemaDefinitionStore _schemas;

        // The POCO graph carries cyclic navigations (domain ↔ schema, supersedes chain);
        // skip back-references so full definitions serialize safely and stay compact.
        private static readonly JsonSerializerSettings Full = new(McpJson.Settings)
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        public MetaDataTools(IAttributeDomainStore domains, ISchemaDefinitionStore schemas)
        {
            _domains = domains;
            _schemas = schemas;
        }

        [McpServerTool]
        [Description("List all attribute domains (entity contracts). Returns name, version, description, attribute count and the linked schema definition for each domain.")]
        public string ListAttributeDomains()
        {
            var entries = _domains.GetAll().Select(d => new
            {
                d.AttributeDomainName,
                d.Version,
                d.Description,
                attributeCount = d.Attributes?.Count ?? 0,
                linkedSchema = d.SchemaDefinition == null ? null : new
                {
                    d.SchemaDefinition.SchemaDefinitionName,
                    d.SchemaDefinition.Version
                }
            });
            return JsonConvert.SerializeObject(entries, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Get an attribute domain's full JSON definition (camelCase) including its attributes and linked schema definition. Accepts the domain name (case-insensitive). Use this before save_attribute_domain to fetch-then-modify an existing domain.")]
        public string GetAttributeDomain([Description("Attribute domain name")] string name)
        {
            var (domain, _) = _domains.GetByName(name);
            if (domain == null) return Error($"Attribute domain '{name}' not found. Use list_attribute_domains to see available domains.");
            return JsonConvert.SerializeObject(domain, Full);
        }

        [McpServerTool]
        [Description(
"""
Create a new attribute domain or replace an existing one with the same name (upsert). The attribute set is replaced wholesale. Returns the stored domain name.
domainJson is the full domain JSON in camelCase — fetch an existing domain first and modify it, or build a new one:
{"attributeDomainName":"orders","version":"1","description":"Customer orders","attributes":[{"attributeName":"orderId","dataType":"Number","displayName":"Order ID"},{"attributeName":"status","dataType":"String"}]}
dataType is one of: String, Boolean, Number, Date, Object, Array.
To link a schema definition, pass schemaName (and optionally schemaVersion — defaults to the latest saved version), or keep the "schemaDefinition" stub from a fetched domain in domainJson when no explicit parameters are given. Omitting both unlinks any existing schema.
""")]
        public string SaveAttributeDomain(
            [Description("Full attribute domain JSON in camelCase (see tool description for shape)")] string domainJson,
            [Description("Optional schema definition name to link; resolves via the schema store. Overrides any schemaDefinition stub inside domainJson.")] string? schemaName = null,
            [Description("Optional schema version to pin; defaults to the latest saved version of schemaName.")] string? schemaVersion = null)
        {
            AttributeDomain domain;
            try
            {
                domain = JsonConvert.DeserializeObject<AttributeDomain>(domainJson, McpJson.Settings);
            }
            catch (Exception ex)
            {
                return Error($"domainJson is not valid JSON: {ex.Message}");
            }
            if (domain == null || string.IsNullOrWhiteSpace(domain.AttributeDomainName))
            {
                return Error("domainJson must contain a non-empty attributeDomainName.");
            }

            SchemaDefinition? schema = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(schemaName))
                {
                    var resolved = _schemas.Get(schemaName, string.IsNullOrWhiteSpace(schemaVersion) ? null : schemaVersion);
                    if (resolved == null) return Error($"Schema definition '{schemaName}'{(string.IsNullOrWhiteSpace(schemaVersion) ? "" : $" version {schemaVersion}")} not found. Use list_schema_definitions to see available schemas.");
                    schema = resolved;
                }
                else if (!string.IsNullOrEmpty(domain.SchemaDefinition?.SchemaDefinitionName))
                {
                    // Fetch-then-modify round-trip: keep the link carried in the payload.
                    schema = domain.SchemaDefinition;
                }

                _domains.Save(domain, schema);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Error(ex.Message);
            }

            return JsonConvert.SerializeObject(new { name = domain.AttributeDomainName }, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Delete an attribute domain by name. Returns whether it existed and was removed.")]
        public string DeleteAttributeDomain([Description("Attribute domain name")] string name)
        {
            var deleted = _domains.Delete(name);
            return JsonConvert.SerializeObject(new { deleted }, McpJson.Settings);
        }

        [McpServerTool]
        [Description("List all saved schema definition versions. Returns name, version and description for each entry (one row per saved version).")]
        public string ListSchemaDefinitions()
        {
            var entries = _schemas.GetAll().Select(s => new
            {
                s.SchemaDefinitionName,
                s.Version,
                s.Description
            });
            return JsonConvert.SerializeObject(entries, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Get a schema definition's full JSON (camelCase) including its Definition body. Accepts the schema name; version is optional and defaults to the latest saved version.")]
        public string GetSchemaDefinition(
            [Description("Schema definition name")] string name,
            [Description("Optional exact version; omit for the latest saved version.")] string? version = null)
        {
            var def = string.IsNullOrWhiteSpace(version) ? _schemas.Get(name) : _schemas.Get(name, version);
            if (def == null) return Error($"Schema definition '{name}'{(string.IsNullOrWhiteSpace(version) ? "" : $" version {version}")} not found. Use list_schema_definitions to see available schemas.");
            return JsonConvert.SerializeObject(def, Full);
        }

        [McpServerTool]
        [Description(
"""
Create a new schema definition version or replace an existing (name, version) pair (upsert). Returns the stored name and version.
schemaJson is the full schema JSON in camelCase:
{"schemaDefinitionName":"orders","version":"2","description":"Orders v2","definition":"{\"type\":\"object\",...}"}
The definition field must be a non-empty string containing valid JSON (the schema body itself, as a string).
""")]
        public string SaveSchemaDefinition([Description("Full schema definition JSON in camelCase (see tool description for shape)")] string schemaJson)
        {
            SchemaDefinition def;
            try
            {
                def = JsonConvert.DeserializeObject<SchemaDefinition>(schemaJson, McpJson.Settings);
            }
            catch (Exception ex)
            {
                return Error($"schemaJson is not valid JSON: {ex.Message}");
            }
            if (def == null || string.IsNullOrWhiteSpace(def.SchemaDefinitionName))
            {
                return Error("schemaJson must contain a non-empty schemaDefinitionName.");
            }

            try
            {
                _schemas.Save(def);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Error(ex.Message);
            }

            return JsonConvert.SerializeObject(new { name = def.SchemaDefinitionName, def.Version }, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Delete one saved version of a schema definition. Returns whether it existed and was removed.")]
        public string DeleteSchemaDefinition(
            [Description("Schema definition name")] string name,
            [Description("Exact version to delete")] string version)
        {
            var deleted = _schemas.Delete(name, version);
            return JsonConvert.SerializeObject(new { deleted }, McpJson.Settings);
        }

        private static string Error(string message) => JsonConvert.SerializeObject(new { error = message });
    }
}
