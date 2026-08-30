using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // JSON FILE FORM DEFINITION STORE — one file per form under the configured directory
    // (forms/{formId}.json), holding a JSON array of version objects. Lock + atomic
    // rewrite; missing dir => empty store + warning. Legacy single-version documents
    // (root object) are upgraded in place to the array layout on load.
    // Mirrors EavRegistryService.SaveToDisk / SshHostStore behavior.
    // ═══════════════════════════════════════════════════════════════════════════════

    public class JsonFileFormDefinitionStore : IFormDefinitionStore
    {
        private static readonly Regex SafeIdRegex = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);

        private readonly string _directory;
        private readonly ILogger<JsonFileFormDefinitionStore>? _logger;
        private readonly object _lock = new();

        public JsonFileFormDefinitionStore(string directory, ILogger<JsonFileFormDefinitionStore>? logger = null)
        {
            _directory = Path.GetFullPath(directory);
            _logger = logger;
            if (!Directory.Exists(_directory))
            {
                _logger?.LogWarning("Forms directory '{Dir}' does not exist - starting with an empty form store", _directory);
                Directory.CreateDirectory(_directory);
            }
        }

        public string ProviderName => "json";

        private static void ValidateId(string formId)
        {
            if (!SafeIdRegex.IsMatch(formId)) throw new ArgumentException($"Invalid form id: '{formId}'");
        }

        private static void ValidateVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("FormDefinition requires a non-empty Version");
        }

        private string PathFor(string formId) => Path.Combine(_directory, $"{formId}.json");

        public IReadOnlyList<FormDefinition> GetAll()
        {
            lock (_lock)
            {
                var forms = new List<FormDefinition>();
                // GetFiles (not EnumerateFiles): the legacy upgrade below rewrites files in place.
                foreach (var file in Directory.GetFiles(_directory, "*.json"))
                {
                    var versions = TryLoadVersions(file);
                    if (versions == null) continue;
                    foreach (var o in versions.OfType<JObject>())
                        if (Map(o) is { } def && !string.IsNullOrEmpty(def.FormId)) forms.Add(def);
                }
                return forms.OrderBy(f => f.FormId).ThenBy(f => VersionSortKey(f.Version)).ToList();
            }
        }

        public FormDefinition? Get(string formId)
        {
            ValidateId(formId);
            lock (_lock)
            {
                var versions = TryLoadVersions(PathFor(formId));
                if (versions == null) return null;
                foreach (var o in versions.OfType<JObject>())
                    if ((bool?)o["IsCurrentVersion"] == true) return Map(o);
                return null;
            }
        }

        public FormDefinition? Get(string formId, string version)
        {
            ValidateId(formId);
            ValidateVersion(version);
            lock (_lock)
            {
                var versions = TryLoadVersions(PathFor(formId));
                if (versions == null) return null;
                foreach (var o in versions.OfType<JObject>())
                    if (string.Equals((string)o["Version"], version, StringComparison.Ordinal)) return Map(o);
                return null;
            }
        }

        public void Save(FormDefinition def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.FormId))
                throw new ArgumentException("FormDefinition requires a non-empty FormId");
            ValidateId(def.FormId);
            ValidateVersion(def.Version);
            if (!(def.Page is JObject page) || !(page["RootElements"] is JArray))
                throw new ArgumentException($"Form '{def.FormId}' Page must be an object with a RootElements array");

            lock (_lock)
            {
                var path = PathFor(def.FormId);
                var versions = TryLoadVersions(path);
                if (versions == null)
                    throw new IOException($"Form file '{path}' is corrupted; delete or repair it before saving");

                // Upsert this version into the array, preserving every other saved version.
                var token = JObject.FromObject(def);
                var replaced = false;
                for (var i = 0; i < versions.Count; i++)
                    if (versions[i] is JObject o && string.Equals((string)o["Version"], def.Version, StringComparison.Ordinal))
                    {
                        versions[i] = token;
                        replaced = true;
                        break;
                    }
                if (!replaced) versions.Add(token);

                // At most one current version per form: clear the flag on every other entry.
                if (def.IsCurrentVersion)
                    foreach (var o in versions.OfType<JObject>().Where(o => !ReferenceEquals(o, token)).ToList())
                        o["IsCurrentVersion"] = false;

                WriteAtomic(path, versions);
            }
        }

        public bool Delete(string formId)
        {
            ValidateId(formId);
            lock (_lock)
            {
                var path = PathFor(formId);
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
        }

        /// <summary>Loads the version array for one form file. A legacy single-version document (root object) is upgraded in place to the array layout. Returns null when the file cannot be read or parsed.</summary>
        private JArray? TryLoadVersions(string path) // caller holds _lock
        {
            if (!File.Exists(path)) return new JArray();

            JToken root;
            try
            {
                root = JToken.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Form file '{Path}' is corrupted", path);
                return null;
            }

            if (root is JArray versions) return versions;
            if (root is JObject legacy)
            {
                // Legacy single-version document: upgrade in place to the array layout.
                if (legacy["Version"] == null) legacy["Version"] = "1";
                if (legacy["IsCurrentVersion"] == null) legacy["IsCurrentVersion"] = true;
                var upgraded = new JArray(legacy);
                try
                {
                    WriteAtomic(path, upgraded);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to upgrade legacy form file '{Path}' to the versioned layout", path);
                }
                return upgraded;
            }

            _logger?.LogWarning("Form file '{Path}' must contain a JSON array of versions or a single-version object", path);
            return null;
        }

        private void WriteAtomic(string path, JToken content) // caller holds _lock
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(content, Formatting.Indented));
            File.Move(temp, path, overwrite: true);
        }

        private static FormDefinition? Map(JObject o) =>
            o.ToObject<FormDefinition>();

        private static int VersionSortKey(string version) => int.TryParse(version, out var n) ? n : int.MaxValue;
    }
}
