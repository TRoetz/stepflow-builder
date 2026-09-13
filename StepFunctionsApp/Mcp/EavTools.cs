using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Mcp
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // MCP EAV TOOLS — read/write captured attribute rows (eav-data/{domain}.json) and
    // manage the registry of known entity contracts. Rows are the downstream data view:
    // form captures, flow outputs and eav://read all land here as flat key/value rows.
    // ═══════════════════════════════════════════════════════════════════════════════

    [McpServerToolType]
    public class EavTools
    {
        private readonly EavRowStore _rows;
        private readonly EavRegistryService _registry;
        private readonly IAttributeDomainStore _domains;

        public EavTools(EavRowStore rows, EavRegistryService registry, IAttributeDomainStore domains)
        {
            _rows = rows;
            _registry = registry;
            _domains = domains;
        }

        [McpServerTool]
        [Description("List all EAV data domains (captured attribute row files). Returns each domain's row count and whether it has a registry entity contract or an attribute domain.")]
        public string ListEavDomains()
        {
            var knownEntities = new HashSet<string>(_registry.GetAllEntities().Select(e => e.EntityName), StringComparer.OrdinalIgnoreCase);
            var knownAttributeDomains = new HashSet<string>(_domains.GetAll().Select(d => d.AttributeDomainName), StringComparer.OrdinalIgnoreCase);

            var entries = _rows.ListDomains().Select(domain => new
            {
                domain,
                rows = _rows.ListRows(domain).Count,
                hasRegistryContract = knownEntities.Contains(domain),
                hasAttributeDomain = knownAttributeDomains.Contains(domain)
            });
            return JsonConvert.SerializeObject(entries, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Read captured EAV rows for a domain in append order. Returns rowKeyId, entityId, entityType, sourceTaskId, capturedAtUtc and the values object for each row. limit caps the number of rows returned (default 100).")]
        public string ReadEavRows(
            [Description("Domain name, e.g. 'orders'")] string domain,
            [Description("Maximum number of rows to return; defaults to 100.")] int? limit = null)
        {
            var cap = Math.Clamp(limit ?? 100, 1, 10_000);
            var rows = _rows.ListRows(domain).Take(cap).Select(r => new
            {
                r.RowKeyId,
                r.EntityId,
                r.EntityType,
                r.SourceTaskId,
                capturedAtUtc = r.CapturedAtUtc.ToString("o"),
                r.Values
            });
            return JsonConvert.SerializeObject(rows, McpJson.Settings);
        }

        [McpServerTool]
        [Description(
"""
Append a new EAV row to a domain. Returns the assigned rowKeyId.
valuesJson is a JSON object of attribute values keyed by attribute name, e.g. {"orderId":42,"status":"new"}.
entityId/entityType/sourceTaskId are optional provenance fields (null for pure capture).
""")]
        public string WriteEavRow(
            [Description("Domain name to append the row to")] string domain,
            [Description("JSON object of attribute values keyed by attribute name")] string valuesJson,
            [Description("Optional entity id this row belongs to.")] string? entityId = null,
            [Description("Optional entity type name for provenance.")] string? entityType = null,
            [Description("Optional human-task / form-capture task id that produced this row.")] string? sourceTaskId = null)
        {
            JObject values;
            try
            {
                values = JObject.Parse(valuesJson);
            }
            catch (Exception ex)
            {
                return Error($"valuesJson is not a valid JSON object: {ex.Message}");
            }

            var row = new EavRow
            {
                Values = values,
                EntityId = entityId,
                EntityType = entityType,
                SourceTaskId = sourceTaskId
            };
            _rows.AppendRow(domain, row); // assigns RowKeyId when empty
            return JsonConvert.SerializeObject(new { row.RowKeyId }, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Replace the values of an existing EAV row by rowKeyId (whole-object replacement). Returns whether the row existed and was updated.")]
        public string UpdateEavRow(
            [Description("Domain name")] string domain,
            [Description("rowKeyId of the row to replace")] string rowKeyId,
            [Description("New JSON object of attribute values keyed by attribute name")] string valuesJson)
        {
            JObject values;
            try
            {
                values = JObject.Parse(valuesJson);
            }
            catch (Exception ex)
            {
                return Error($"valuesJson is not a valid JSON object: {ex.Message}");
            }

            var updated = _rows.UpdateRow(domain, rowKeyId, values);
            return JsonConvert.SerializeObject(new { updated }, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Merge a partial set of attribute values into an existing EAV row by rowKeyId (existing keys overwritten, others kept). Returns whether the row existed and was patched.")]
        public string PatchEavRow(
            [Description("Domain name")] string domain,
            [Description("rowKeyId of the row to patch")] string rowKeyId,
            [Description("JSON object with only the attribute values to add or overwrite")] string patchJson)
        {
            JObject patch;
            try
            {
                patch = JObject.Parse(patchJson);
            }
            catch (Exception ex)
            {
                return Error($"patchJson is not a valid JSON object: {ex.Message}");
            }

            var patched = _rows.PatchRow(domain, rowKeyId, patch);
            return JsonConvert.SerializeObject(new { patched }, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Delete an EAV row by domain and rowKeyId. Returns whether the row existed and was removed.")]
        public string DeleteEavRow(
            [Description("Domain name")] string domain,
            [Description("rowKeyId of the row to remove")] string rowKeyId)
        {
            var deleted = _rows.RemoveRow(domain, rowKeyId);
            return JsonConvert.SerializeObject(new { deleted }, McpJson.Settings);
        }

        [McpServerTool]
        [Description("List all registry entity contracts (known entities). Returns each entity's name, description and full attribute definitions (attributeName, dataType, isRequired, defaultValue, jsonPathMapping).")]
        public string ListEavEntities()
        {
            var entries = _registry.GetAllEntities().Select(e => new
            {
                e.EntityName,
                e.Description,
                attributes = e.Attributes.Select(a => new
                {
                    a.AttributeName,
                    a.DataType,
                    a.IsRequired,
                    a.DefaultValue,
                    a.JsonPathMapping
                })
            });
            return JsonConvert.SerializeObject(entries, McpJson.Settings);
        }

        [McpServerTool]
        [Description(
"""
Register (or replace) an entity contract in the EAV registry. Returns the stored entity name.
entityJson is the full entity JSON in camelCase:
{"entityName":"orders","description":"Customer orders","attributes":[{"attributeName":"orderId","dataType":"number","isRequired":true,"jsonPathMapping":"$.id"},{"attributeName":"status","dataType":"string"}]}
""")]
        public string RegisterEavEntity([Description("Full entity contract JSON in camelCase (see tool description for shape)")] string entityJson)
        {
            EavEntityDefinition entity;
            try
            {
                entity = JsonConvert.DeserializeObject<EavEntityDefinition>(entityJson, McpJson.Settings);
            }
            catch (Exception ex)
            {
                return Error($"entityJson is not valid JSON: {ex.Message}");
            }
            if (entity == null || string.IsNullOrWhiteSpace(entity.EntityName))
            {
                return Error("entityJson must contain a non-empty entityName.");
            }

            _registry.RegisterEntity(entity);
            return JsonConvert.SerializeObject(new { name = entity.EntityName }, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Delete an entity contract from the EAV registry by name. Returns whether it existed and was removed.")]
        public string DeleteEavEntity([Description("Entity name to remove from the registry")] string name)
        {
            var exists = _registry.GetEntity(name) != null;
            if (exists) _registry.DeleteEntity(name);
            return JsonConvert.SerializeObject(new { deleted = exists }, McpJson.Settings);
        }

        private static string Error(string message) => JsonConvert.SerializeObject(new { error = message });
    }
}
