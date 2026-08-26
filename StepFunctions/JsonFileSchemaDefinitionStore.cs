using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // JSON FILE SCHEMA DEFINITION STORE — registry file schema_definitions.json holding
    // a top-level array of camelCase SchemaDefinition entries (one per saved version).
    // Lock + atomic rewrite; missing file ⇒ empty registry + warning log. Mirrors the
    // JsonFileAttributeDomainStore conventions exactly.
    // ═══════════════════════════════════════════════════════════════════════════════

    public class JsonFileSchemaDefinitionStore : ISchemaDefinitionStore
    {
        private static readonly JsonSerializerSettings CamelCase = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented
        };

        private readonly string _registryPath;
        private readonly ILogger<JsonFileSchemaDefinitionStore>? _logger;
        private readonly object _lock = new();
        private List<SchemaDefinition> _entries;

        public JsonFileSchemaDefinitionStore(string registryPath, ILogger<JsonFileSchemaDefinitionStore>? logger = null)
        {
            _registryPath = Path.GetFullPath(registryPath);
            _logger = logger;
            var dir = Path.GetDirectoryName(_registryPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(_registryPath))
            {
                try
                {
                    _entries = JsonConvert.DeserializeObject<List<SchemaDefinition>>(File.ReadAllText(_registryPath), CamelCase) ?? new();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Schema definition registry '{Path}' is corrupted — starting empty", _registryPath);
                    _entries = new List<SchemaDefinition>();
                }
            }
            else
            {
                _logger?.LogWarning("Schema definition registry '{Path}' does not exist — starting with an empty registry", _registryPath);
                _entries = new List<SchemaDefinition>();
            }
        }

        public string ProviderName => "json";

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("SchemaDefinition requires a non-empty SchemaDefinitionName");
        }

        private static void ValidateVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("SchemaDefinition requires a non-empty Version");
        }

        private SchemaDefinition? FindEntry(string name, string? version = null) => // caller holds _lock
            _entries.FirstOrDefault(e =>
                string.Equals(e.SchemaDefinitionName, name, StringComparison.OrdinalIgnoreCase) &&
                (version == null || string.Equals(e.Version, version, StringComparison.Ordinal)));
        private static int VersionSortKey(string version) => int.TryParse(version, out var n) ? n : int.MaxValue;

        public IReadOnlyList<SchemaDefinition> GetAll()
        {
            lock (_lock)
            {
                return _entries
                    .OrderBy(e => e.SchemaDefinitionName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => VersionSortKey(e.Version))
                    .ToList();
            }
        }

        public SchemaDefinition? Get(string name)
        {
            ValidateName(name);
            lock (_lock)
            {
                SchemaDefinition? latest = null;
                foreach (var e in _entries)
                    if (string.Equals(e.SchemaDefinitionName, name, StringComparison.OrdinalIgnoreCase) &&
                        (latest == null || VersionSortKey(e.Version) > VersionSortKey(latest.Version)))
                        latest = e;
                return latest;
            }
        }

        public SchemaDefinition? Get(string name, string version)
        {
            ValidateName(name);
            ValidateVersion(version);
            lock (_lock)
            {
                return FindEntry(name, version);
            }
        }

        public void Save(SchemaDefinition def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.SchemaDefinitionName))
                throw new ArgumentException("SchemaDefinition requires a non-empty SchemaDefinitionName");
            ValidateVersion(def.Version);
            if (string.IsNullOrWhiteSpace(def.Definition))
                throw new ArgumentException($"Schema '{def.SchemaDefinitionName}' requires a non-empty Definition");
            try
            {
                JToken.Parse(def.Definition);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Schema '{def.SchemaDefinitionName}' Definition must be valid JSON", ex);
            }

            lock (_lock)
            {
                var existing = FindEntry(def.SchemaDefinitionName, def.Version);
                if (existing == null) _entries.Add(def);
                else _entries[_entries.IndexOf(existing)] = def;
                WriteToDisk();
            }
        }

        public bool Delete(string name, string version)
        {
            ValidateName(name);
            ValidateVersion(version);
            lock (_lock)
            {
                var entry = FindEntry(name, version);
                if (entry == null) return false;
                _entries.Remove(entry);
                WriteToDisk();
                return true;
            }
        }

        private void WriteToDisk() // caller holds _lock
        {
            // The POCO's navigation collections are EF-style metadata; they never persist in file form.
            var serializer = JsonSerializer.Create(CamelCase);
            var payload = _entries.Select(e =>
            {
                var o = (JObject)JToken.FromObject(e, serializer)!;
                o.Remove("AttributeDomains");
                o.Remove("ParentRelations");
                o.Remove("ChildRelations");
                return o;
            }).ToList();

            var temp = _registryPath + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(payload, CamelCase));
            File.Move(temp, _registryPath, overwrite: true);
        }
    }
}
