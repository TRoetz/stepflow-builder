using System.Text.RegularExpressions;
using Newtonsoft.Json;
using StepFlow.DataModel.Entities.DataSource;

namespace StepFunctionsApp.DataExchange;

/// <summary>
/// File-based registry of Data Exchange profiles. Profiles live in the workspace tree — one JSON
/// document per profile at {sub-project}/data-exchange/{id}/profile.json — plus an "unassigned"
/// bucket ({workspaceRoot}/{id}/profile.json) for profiles saved without a location, and an optional
/// legacy flat directory (one {id}.json per profile). Profile identity is
/// <see cref="DataExchangeProfile.ProfileId"/> (falling back to a slug of the name), which doubles as
/// the id in dataexchange:// URIs.
/// </summary>
public class DataExchangeProfileStore
{
    // The POCO graph carries cyclic navigation properties; only forward references matter for persistence.
    private static readonly JsonSerializerSettings Settings = new()
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented
    };

    /// <summary>A discovered profile: the document, its workspace location (null = unassigned), and file path.</summary>
    public sealed record ProfileEntry(DataExchangeProfile Profile, string? SubProjectPath, string FilePath);

    private readonly string _root;
    private readonly string? _legacyDir;

    /// <param name="workspaceRoot">Workspace tree root; organized + unassigned profiles live here.</param>
    /// <param name="legacyProfilesDirectory">Optional legacy flat directory (one {id}.json per profile).</param>
    public DataExchangeProfileStore(string workspaceRoot, string? legacyProfilesDirectory = null)
    {
        _root = Path.GetFullPath(workspaceRoot);
        _legacyDir = string.IsNullOrWhiteSpace(legacyProfilesDirectory) ? null : Path.GetFullPath(legacyProfilesDirectory);
    }

    /// <summary>All profiles (workspace tree first, then legacy dir), deduped by resolved id and ordered by it.</summary>
    public IReadOnlyList<DataExchangeProfile> LoadAll() => ScanAll().Select(e => e.Profile).ToList();

    /// <summary>
    /// Discovers every profile: unassigned bare files at the workspace root ({root}/*.json), any
    /// profile.json under a data-exchange folder (organized, subProjectPath = node path above it) or
    /// directly under the root (unassigned folder shape {root}/{id}/profile.json), then the legacy
    /// flat dir. Duplicates by resolved id keep the first occurrence — workspace wins over legacy.
    /// </summary>
    public List<ProfileEntry> ScanAll()
    {
        var entries = new List<ProfileEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? subProjectPath, string file)
        {
            DataExchangeProfile profile;
            try
            {
                profile = Deserialize(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load DataExchange profile '{file}': {ex.Message}", ex);
            }

            var id = ResolveId(profile);
            if (!seen.Add(id)) return; // workspace entries are scanned before legacy — they win on duplicate ids
            entries.Add(new ProfileEntry(profile, subProjectPath, file));
        }

        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.GetFiles(_root, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                Add(null, file); // unassigned bare files at the workspace root

            foreach (var file in Directory.EnumerateFiles(_root, "profile.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var idDir = Path.GetDirectoryName(file)!;
                var container = Path.GetDirectoryName(idDir);
                if (container == null) continue;

                string? subProjectPath;
                if (string.Equals(Path.GetFileName(container), "data-exchange", StringComparison.OrdinalIgnoreCase))
                {
                    // Organized: the node path is the parent of the data-exchange dir, relative to root.
                    var nodeDir = Path.GetDirectoryName(container);
                    if (nodeDir == null) continue;
                    subProjectPath = RelativeNodePath(nodeDir);
                    if (string.IsNullOrEmpty(subProjectPath)) subProjectPath = null; // guard: data-exchange directly under the root
                }
                else if (SameDirectory(container, _root))
                {
                    subProjectPath = null; // unassigned folder shape: {root}/{id}/profile.json
                }
                else continue; // anything else is not a profile location

                Add(subProjectPath, file);
            }
        }

        if (_legacyDir != null && Directory.Exists(_legacyDir))
        {
            foreach (var file in Directory.GetFiles(_legacyDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                Add(null, file);
        }

        return entries.OrderBy(e => ResolveId(e.Profile), StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Resolves a profile by id or name. Fast paths for {root}/{id}.json + legacy file, then scan match on resolved id or name (case-insensitive).</summary>
    public DataExchangeProfile? Get(string idOrName)
    {
        var safe = SanitizeId(idOrName);
        if (safe.Length > 0 && safe is not ("." or ".."))
        {
            var direct = Path.Combine(_root, $"{safe}.json");
            if (File.Exists(direct)) return Deserialize(File.ReadAllText(direct));

            if (_legacyDir != null)
            {
                var legacy = Path.Combine(_legacyDir, $"{safe}.json");
                if (File.Exists(legacy)) return Deserialize(File.ReadAllText(legacy));
            }
        }

        return ScanAll().FirstOrDefault(e =>
            string.Equals(ResolveId(e.Profile), idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Profile.DataExchangeProfileName, idOrName, StringComparison.OrdinalIgnoreCase))?.Profile;
    }

    /// <summary>
    /// Persists the profile and returns its resolved id. An existing profile is updated in place
    /// wherever it lives; new ones go to {sub}/data-exchange/{id}/profile.json when a location is
    /// given, else the unassigned bucket {root}/{id}/profile.json.
    /// </summary>
    public string Save(DataExchangeProfile profile, string? subProjectPath = null)
    {
        var id = ResolveId(profile);

        var existing = ScanAll().FirstOrDefault(e => string.Equals(ResolveId(e.Profile), id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            File.WriteAllText(existing.FilePath, Serialize(profile));
            return id;
        }

        var dir = !string.IsNullOrWhiteSpace(subProjectPath)
            ? Path.Combine(_root, subProjectPath.Replace('/', Path.DirectorySeparatorChar), "data-exchange", id)
            : Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "profile.json"), Serialize(profile));
        return id;
    }

    /// <summary>Removes the profile file and its now-empty {id} folder (never anything above).</summary>
    public bool Delete(string idOrName)
    {
        var entry = ScanAll().FirstOrDefault(e =>
            string.Equals(ResolveId(e.Profile), idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Profile.DataExchangeProfileName, idOrName, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return false;

        File.Delete(entry.FilePath);

        // Only profile.json files live in a per-id folder — bare {id}.json files have no folder to clean up.
        if (string.Equals(Path.GetFileName(entry.FilePath), "profile.json", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(entry.FilePath)!;
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch
            {
                // best effort — the profile file itself is already gone
            }
        }

        return true;
    }

    public static string ResolveId(DataExchangeProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.ProfileId) ? SanitizeId(profile.ProfileId!) : Slug(profile.DataExchangeProfileName ?? "profile");

    private static string Slug(string name)
    {
        var slug = Regex.Replace(name, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
        return string.IsNullOrEmpty(slug) ? "profile" : slug;
    }

    public static string SanitizeId(string id) => Regex.Replace(id, "[^A-Za-z0-9._-]", "");

    private static DataExchangeProfile Deserialize(string json) =>
        JsonConvert.DeserializeObject<DataExchangeProfile>(json, Settings)!;

    private static string Serialize(DataExchangeProfile profile) =>
        JsonConvert.SerializeObject(profile, Settings);

    /// <summary>Node path (org/project/sub-project, '/'-separated) of a directory relative to the workspace root.</summary>
    private string RelativeNodePath(string dir)
    {
        var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, '/');
        var rootFull = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar, '/');
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "";
        var relative = full.Substring(rootFull.Length).TrimStart(Path.DirectorySeparatorChar, '/');
        return relative.Replace('\\', '/');
    }

    private static bool SameDirectory(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
