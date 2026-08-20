using System.Text.RegularExpressions;
using Newtonsoft.Json;
using StepFlow.DataModel.Entities.DataSource;

namespace StepFunctionsApp.DataExchange;

/// <summary>
/// File-based registry of Data Exchange profiles - one JSON document per profile under the
/// configured profiles directory. Profile identity is <see cref="DataExchangeProfile.ProfileId"/>
/// (falling back to a slug of the name), which doubles as the id in dataexchange:// URIs.
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

    private readonly string _profilesDir;

    public DataExchangeProfileStore(string profilesDirectory) => _profilesDir = profilesDirectory;

    public IReadOnlyList<DataExchangeProfile> LoadAll()
    {
        if (!Directory.Exists(_profilesDir)) return Array.Empty<DataExchangeProfile>();

        var list = new List<DataExchangeProfile>();
        foreach (var file in Directory.GetFiles(_profilesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                list.Add(Deserialize(File.ReadAllText(file)));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load DataExchange profile '{Path.GetFileNameWithoutExtension(file)}': {ex.Message}", ex);
            }
        }

        return list;
    }

    /// <summary>Resolves a profile by id or name. Ids match the file stem first, then any stored ProfileId/Name.</summary>
    public DataExchangeProfile? Get(string idOrName)
    {
        var direct = Path.Combine(_profilesDir, $"{SanitizeId(idOrName)}.json");
        if (File.Exists(direct)) return Deserialize(File.ReadAllText(direct));

        return LoadAll().FirstOrDefault(p =>
            string.Equals(ResolveId(p), idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.DataExchangeProfileName, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Persists the profile and returns its resolved id.</summary>
    public string Save(DataExchangeProfile profile)
    {
        Directory.CreateDirectory(_profilesDir);
        var id = ResolveId(profile);
        File.WriteAllText(Path.Combine(_profilesDir, $"{id}.json"), JsonConvert.SerializeObject(profile, Settings));
        return id;
    }

    public bool Delete(string idOrName)
    {
        var profile = Get(idOrName);
        if (profile == null) return false;

        var file = Path.Combine(_profilesDir, $"{ResolveId(profile)}.json");
        if (!File.Exists(file)) return false;
        File.Delete(file);
        return true;
    }

    public static string ResolveId(DataExchangeProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.ProfileId) ? SanitizeId(profile.ProfileId!) : Slug(profile.DataExchangeProfileName ?? "profile");

    private static string Slug(string name)
    {
        var slug = Regex.Replace(name, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
        return string.IsNullOrEmpty(slug) ? "profile" : slug;
    }

    private static string SanitizeId(string id) => Regex.Replace(id, "[^A-Za-z0-9._-]", "");

    private static DataExchangeProfile Deserialize(string json) =>
        JsonConvert.DeserializeObject<DataExchangeProfile>(json, Settings)!;
}
