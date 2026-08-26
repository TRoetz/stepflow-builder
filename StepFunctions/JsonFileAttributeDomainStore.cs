using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using StepFlow.DataModel.Entities.MetaData;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // JSON FILE ATTRIBUTE DOMAIN STORE — registry file attribute_domains.json holding a
    // top-level array of { schemaDefinition?, attributeDomain } entries. Lock + atomic
    // rewrite; missing file ⇒ empty registry + warning log (mirrors EavRegistryService).
    // ═══════════════════════════════════════════════════════════════════════════════

    public class JsonFileAttributeDomainStore : IAttributeDomainStore
    {
        private static readonly JsonSerializerSettings CamelCase = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented
        };

        private readonly string _registryPath;
        private readonly ILogger<JsonFileAttributeDomainStore>? _logger;
        private readonly object _lock = new();
        private List<AttributeDomainEntry> _entries;

        public JsonFileAttributeDomainStore(string registryPath, ILogger<JsonFileAttributeDomainStore>? logger = null)
        {
            _registryPath = Path.GetFullPath(registryPath);
            _logger = logger;
            var dir = Path.GetDirectoryName(_registryPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(_registryPath))
            {
                try
                {
                    _entries = JsonConvert.DeserializeObject<List<AttributeDomainEntry>>(File.ReadAllText(_registryPath), CamelCase) ?? new();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Attribute domain registry '{Path}' is corrupted — starting empty", _registryPath);
                    _entries = new List<AttributeDomainEntry>();
                }
            }
            else
            {
                _logger?.LogWarning("Attribute domain registry '{Path}' does not exist — starting with an empty registry", _registryPath);
                _entries = new List<AttributeDomainEntry>();
            }
        }

        public string ProviderName => "json";

        private AttributeDomainEntry? FindEntry(string domainName) =>
            _entries.FirstOrDefault(e => string.Equals(e.AttributeDomain.AttributeDomainName, domainName, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<AttributeDomain> GetAll()
        {
            lock (_lock)
                return _entries.Select(e =>
                {
                    var (domain, schema) = AttributeDomainMapping.ToPoco(e);
                    domain.SchemaDefinition = schema;
                    return domain;
                }).OrderBy(d => d.AttributeDomainName).ToList();
        }

        public (AttributeDomain? domain, SchemaDefinition? schema) GetByName(string domainName)
        {
            lock (_lock)
            {
                var entry = FindEntry(domainName);
                if (entry == null) return (null, null);
                return AttributeDomainMapping.ToPoco(entry);
            }
        }

        public void Save(AttributeDomain domain, SchemaDefinition? schema = null)
        {
            if (domain == null || string.IsNullOrWhiteSpace(domain.AttributeDomainName))
                throw new ArgumentException("AttributeDomain requires a non-empty AttributeDomainName");

            lock (_lock)
            {
                var entry = FindEntry(domain.AttributeDomainName);
                if (entry == null)
                {
                    entry = new AttributeDomainEntry();
                    _entries.Add(entry);
                }
                entry.SchemaDefinition = schema == null || string.IsNullOrEmpty(schema.SchemaDefinitionName)
                    ? null
                    : new SchemaDefinitionRef
                    {
                        SchemaDefinitionName = schema.SchemaDefinitionName,
                        Version = schema.Version ?? "1",
                        Description = schema.Description
                    };
                entry.AttributeDomain = AttributeDomainMapping.FromPoco(domain, schema).AttributeDomain;

                WriteToDisk();
            }
        }

        public bool Delete(string domainName)
        {
            lock (_lock)
            {
                var entry = FindEntry(domainName);
                if (entry == null) return false;
                _entries.Remove(entry);
                WriteToDisk();
                return true;
            }
        }

        private void WriteToDisk()
        {
            var temp = _registryPath + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(_entries, CamelCase));
            File.Move(temp, _registryPath, overwrite: true);
        }
    }
}
