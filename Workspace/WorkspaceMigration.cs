using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StepFlow.DataModel.Entities.DataSource;
using StepFunctionsApp.DataExchange;

namespace StepFunctionsApp.Workspace;

/// <summary>
/// One-time migration into the workspace tree: ensures {Default}/{Default}/{Default} exists and moves
/// each legacy flat profile ({legacyProfilesDir}/*.json) into {sub}/data-exchange/{id}/profile.json.
/// Idempotent — a target that already exists is skipped, so re-running is a no-op.
/// </summary>
public static class WorkspaceMigration
{
    private static readonly JsonSerializerSettings ProfileSettings = new()
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore
    };

    public static void Migrate(WorkspaceStore store, string? legacyProfilesDir, ILogger? logger = null, string defaultNodeName = "Default")
    {
        var subPath = WorkspaceStore.Join(defaultNodeName, defaultNodeName, defaultNodeName);
        store.EnsureSubProject(subPath);
        logger?.LogInformation("Workspace ready at {Root} (default sub-project: {Sub})", store.Root, subPath);

        if (string.IsNullOrWhiteSpace(legacyProfilesDir) || !Directory.Exists(legacyProfilesDir)) return;

        var targetDataExchange = Path.Combine(store.Root, subPath.Replace('/', Path.DirectorySeparatorChar), "data-exchange");
        foreach (var file in Directory.GetFiles(legacyProfilesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var id = ResolveLegacyId(file);
            if (id.Length == 0)
            {
                logger?.LogWarning("Skipping unmigratable profile file {File} (no resolvable id)", file);
                continue;
            }

            var targetDir = Path.Combine(targetDataExchange, id);
            var targetFile = Path.Combine(targetDir, "profile.json");
            if (File.Exists(targetFile))
            {
                logger?.LogDebug("Profile {Id} already migrated — skipping {File}", id, file);
                continue;
            }

            Directory.CreateDirectory(targetDir);
            File.Move(file, targetFile);
            logger?.LogInformation("Migrated profile {Id} into {Sub}/data-exchange", id, subPath);
        }
    }

    /// <summary>Target folder id: the profile's resolved id when parseable, else the sanitized file stem.</summary>
    private static string ResolveLegacyId(string file)
    {
        try
        {
            var profile = JsonConvert.DeserializeObject<DataExchangeProfile>(File.ReadAllText(file), ProfileSettings);
            if (profile != null) return DataExchangeProfileStore.ResolveId(profile);
        }
        catch
        {
            // fall through to the file stem below
        }

        return WorkspaceStore.SanitizeId(Path.GetFileNameWithoutExtension(file));
    }
}
