using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace StepFunctionsApp.Workspace;

/// <summary>
/// File-based workspace tree: organization → project → sub-project (max depth 3, derived from path
/// segment count). Each node carries an optional access.json ACL; flows and data-exchange profiles
/// live under the selected sub-project. Node paths are '/'-separated relative to the root
/// (e.g. "Acme/Website/Web").
/// </summary>
public class WorkspaceStore
{
    /// <summary>Maximum node depth: 1 = org, 2 = project, 3 = sub-project.</summary>
    public const int MaxDepth = 3;

    // Indented camelCase for persisted documents (access.json / meta.json).
    private static readonly JsonSerializerSettings FileSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    private readonly string _root;

    /// <summary>Absolute path of the workspace root.</summary>
    public string Root => _root;

    public WorkspaceStore(string rootDirectory) => _root = Path.GetFullPath(rootDirectory);

    // ── Static helpers ────────────────────────────────────────────────────────────────────────

    /// <summary>Joins node path segments with '/' (the workspace path separator).</summary>
    public static string Join(params string[] segments) =>
        string.Join('/', segments.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Depth of a node path: 1 = org, 2 = project, 3 = sub-project; empty → 0.</summary>
    public static int DepthOf(string? nodePath) => SplitPath(nodePath).Length;

    /// <summary>Ancestor paths root-first ("Acme", "Acme/Website", …).</summary>
    public static IEnumerable<string> AncestorPaths(string nodePath)
    {
        var parts = SplitPath(nodePath);
        for (var i = 1; i <= parts.Length; i++)
            yield return string.Join('/', parts.Take(i));
    }

    /// <summary>Same id sanitization as DataExchangeProfileStore: strip [^A-Za-z0-9._-].</summary>
    public static string SanitizeId(string? id) => Regex.Replace(id ?? "", "[^A-Za-z0-9._-]", "");

    private static string[] SplitPath(string? nodePath) =>
        (nodePath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);

    // ── Listing / CRUD ────────────────────────────────────────────────────────────────────────

    /// <summary>Directory names directly under a workspace node, dot-dirs skipped, ordinal-ignore-case sorted.</summary>
    private static IReadOnlyList<string> DirNames(string parent) =>
        Directory.Exists(parent)
            ? Directory.GetDirectories(parent)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

    public IReadOnlyList<string> ListOrgs() => DirNames(_root);

    public IReadOnlyList<string> ListProjects(string org) => DirNames(FullPath(org));

    public IReadOnlyList<string> ListSubProjects(string org, string project) =>
        DirNames(FullPath(Join(org, project)));

    /// <summary>True when the node exists on disk (and stays inside the workspace root).</summary>
    public bool NodeExists(string? nodePath)
    {
        if (string.IsNullOrWhiteSpace(nodePath)) return false;
        try { return Directory.Exists(FullPath(nodePath)); }
        catch (InvalidOperationException) { return false; } // escapes the root
    }

    /// <summary>Creates a child node under <paramref name="parentPath"/> (or at the root). Returns the new node's relative path.</summary>
    public string CreateNode(string name, string? parentPath = null)
    {
        ValidateName(name);
        var depth = DepthOf(parentPath) + 1;
        if (depth > MaxDepth)
            throw new InvalidOperationException($"Maximum workspace depth is {MaxDepth} (org/project/sub-project); cannot create '{name}' under '{parentPath}'");

        if (!string.IsNullOrWhiteSpace(parentPath))
        {
            var parentFull = FullPath(parentPath); // throws when the parent escapes the root
            if (!Directory.Exists(parentFull)) throw new DirectoryNotFoundException($"Parent node '{parentPath}' does not exist");
        }

        var path = string.IsNullOrWhiteSpace(parentPath) ? name : Join(parentPath, name);
        var full = Directory.CreateDirectory(FullPath(path)).FullName;
        if (depth == MaxDepth)
        {
            // Sub-projects carry their content directories from birth.
            Directory.CreateDirectory(Path.Combine(full, "flows"));
            Directory.CreateDirectory(Path.Combine(full, "data-exchange"));
        }
        return path;
    }

    /// <summary>Recursively deletes a node (children + ACLs included). Returns false when the node is missing.</summary>
    public bool DeleteNode(string? nodePath)
    {
        if (string.IsNullOrWhiteSpace(nodePath)) return false;
        var full = FullPath(nodePath); // throws on escape; caller maps to error
        if (!Directory.Exists(full)) return false;
        Directory.Delete(full, recursive: true);
        return true;
    }

    /// <summary>Renames a node in place (children + ACLs move with it). Rejects an existing target. Returns the new relative path.</summary>
    public string RenameNode(string nodePath, string newName)
    {
        ValidateName(newName);
        var full = FullPath(nodePath); // throws on escape; caller maps to error
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Workspace node '{nodePath}' does not exist");

        var parts = SplitPath(nodePath);
        var targetPath = string.Join('/', parts.Take(parts.Length - 1).Append(newName));
        var targetFull = FullPath(targetPath);
        if (Directory.Exists(targetFull)) throw new InvalidOperationException($"Target node '{targetPath}' already exists");

        Directory.Move(full, targetFull);
        return targetPath;
    }

    private static void ValidateName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || trimmed is "." or ".." || trimmed.Contains('/') ||
            trimmed.Any(Path.GetInvalidFileNameChars().Contains))
            throw new InvalidOperationException($"Invalid node name '{name}'");
    }

    // ── ACLs and inheritance ──────────────────────────────────────────────────────────────────

    private string AclFile(string nodePath) => Path.Combine(FullPath(nodePath), "access.json");

    /// <summary>Loads a node's local ACL; a missing file yields an empty ACL.</summary>
    public NodeAcl LoadAcl(string nodePath)
    {
        var file = AclFile(nodePath);
        if (!File.Exists(file)) return new NodeAcl();
        try
        {
            return JsonConvert.DeserializeObject<NodeAcl>(File.ReadAllText(file), FileSettings) ?? new NodeAcl();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load ACL for '{nodePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Saves a node's local ACL: principals are trimmed, roles normalized via AccessRole.TryParse,
    /// and duplicates per principal (case-insensitive) collapse to the highest rank. The node must exist.
    /// </summary>
    public void SaveAcl(string nodePath, IEnumerable<AccessEntry>? entries)
    {
        if (!NodeExists(nodePath)) throw new DirectoryNotFoundException($"Workspace node '{nodePath}' does not exist");

        var merged = new Dictionary<string, AccessEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries ?? Array.Empty<AccessEntry>())
        {
            if (entry == null) continue;
            var principal = (entry.Principal ?? "").Trim();
            if (principal.Length == 0) continue;
            AccessRole.TryParse(entry.Role, out var role);

            if (!merged.TryGetValue(principal, out var existing))
            {
                merged[principal] = new AccessEntry { Principal = principal, Role = role };
            }
            else if (AccessRole.Rank(role) > AccessRole.Rank(existing.Role))
            {
                existing.Role = role; // keep the first spelling, take the higher rank
            }
        }

        File.WriteAllText(AclFile(nodePath), JsonConvert.SerializeObject(new NodeAcl { Entries = merged.Values.ToList() }, FileSettings));
    }

    /// <summary>
    /// Effective grants for a node: merges the local ACLs along org → project → sub-project,
    /// higher rank wins per principal (case-insensitive). Each grant records the sourcePath of the
    /// winning entry so the UI can label inherited vs local. Ordered by principal.
    /// </summary>
    public IReadOnlyList<EffectiveGrant> GetEffectiveGrants(string nodePath)
    {
        var merged = new Dictionary<string, EffectiveGrant>(StringComparer.OrdinalIgnoreCase);
        foreach (var ancestor in AncestorPaths(nodePath))
        {
            foreach (var entry in LoadAcl(ancestor).Entries)
            {
                if (!merged.TryGetValue(entry.Principal, out var existing) || AccessRole.Rank(entry.Role) > AccessRole.Rank(existing.Role))
                    merged[entry.Principal] = new EffectiveGrant { Principal = entry.Principal, Role = entry.Role, SourcePath = ancestor };
            }
        }

        return merged.Values.OrderBy(g => g.Principal, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ── Flows under a sub-project ─────────────────────────────────────────────────────────────

    /// <summary>Lists flows of a sub-project (scans flows/*/meta.json; folders without meta are skipped).</summary>
    public IReadOnlyList<(string Id, WorkspaceFlowMeta Meta)> ListFlows(string subProjectPath)
    {
        var flowsDir = FullPath(Join(subProjectPath, "flows"));
        if (!Directory.Exists(flowsDir)) return Array.Empty<(string, WorkspaceFlowMeta)>();

        var result = new List<(string Id, WorkspaceFlowMeta Meta)>();
        foreach (var dir in Directory.GetDirectories(flowsDir).Where(d => !Path.GetFileName(d).StartsWith('.')))
        {
            var id = Path.GetFileName(dir);
            var metaFile = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaFile)) continue;
            try
            {
                var meta = JsonConvert.DeserializeObject<WorkspaceFlowMeta>(File.ReadAllText(metaFile), FileSettings);
                if (meta != null) result.Add((id, meta));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load flow metadata '{subProjectPath}/flows/{id}': {ex.Message}", ex);
            }
        }

        return result.OrderBy(f => f.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Loads a flow's ASL document (flow.json) as raw JSON; null when missing.</summary>
    public string? LoadFlowDefinition(string subProjectPath, string flowId)
    {
        var file = FlowFile(subProjectPath, flowId);
        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    /// <summary>
    /// Saves a flow under a sub-project. Semantics: an explicit id that already exists updates in
    /// place (createdAt preserved); otherwise a same-name flow under this sub-project is updated and
    /// its id reused (mirrors localStorage save-by-name); otherwise a new flow is created with
    /// id = requestedId ?? slug(name), uniquified with -2, -3 suffixes. Returns (id, created).
    /// </summary>
    public (string Id, bool Created) SaveFlow(string subProjectPath, string? requestedId, string name, string? description, string definitionJson)
    {
        var flowsDir = FullPath(Join(subProjectPath, "flows"));
        if (!Directory.Exists(flowsDir)) throw new DirectoryNotFoundException($"Sub-project '{subProjectPath}' has no flows directory");

        // 1. Explicit id that exists → update in place.
        string? targetId = null;
        var safeRequested = SanitizeId(requestedId);
        if (safeRequested.Length > 0 && Directory.Exists(Path.Combine(flowsDir, safeRequested)))
            targetId = safeRequested;

        // 2. Same-name flow under this sub-project → update it (reuse its id).
        if (targetId == null)
        {
            foreach (var (id, m) in ListFlows(subProjectPath))
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                { targetId = id; break; }
        }

        // 3. Otherwise create new with a uniquified id.
        DateTime createdAt;
        bool created;
        if (targetId != null)
        {
            var metaFile = Path.Combine(flowsDir, targetId, "meta.json");
            createdAt = File.Exists(metaFile)
                ? JsonConvert.DeserializeObject<WorkspaceFlowMeta>(File.ReadAllText(metaFile), FileSettings)?.CreatedAt ?? DateTime.UtcNow
                : DateTime.UtcNow;
            created = false;
        }
        else
        {
            var baseId = safeRequested.Length > 0 ? safeRequested : Slug(name);
            targetId = Uniquify(flowsDir, baseId);
            createdAt = DateTime.UtcNow;
            created = true;
        }

        var dir = Path.Combine(flowsDir, targetId!);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "flow.json"), definitionJson);
        var meta = new WorkspaceFlowMeta { Name = name, Description = description, CreatedAt = createdAt, UpdatedAt = DateTime.UtcNow };
        File.WriteAllText(Path.Combine(dir, "meta.json"), JsonConvert.SerializeObject(meta, FileSettings));
        return (targetId!, created);
    }

    /// <summary>Deletes a flow folder. Returns false when missing.</summary>
    public bool DeleteFlow(string subProjectPath, string flowId)
    {
        var dir = Path.GetDirectoryName(FlowFile(subProjectPath, flowId))!;
        if (!Directory.Exists(dir)) return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    private string FlowFile(string subProjectPath, string flowId)
    {
        var safe = SanitizeId(flowId);
        if (safe.Length == 0 || safe is "." or "..") throw new InvalidOperationException($"Invalid flow id '{flowId}'");
        return Path.Combine(FullPath(Join(subProjectPath, "flows")), safe, "flow.json");
    }

    private static string Uniquify(string flowsDir, string baseId)
    {
        if (!Directory.Exists(Path.Combine(flowsDir, baseId))) return baseId;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseId}-{i}";
            if (!Directory.Exists(Path.Combine(flowsDir, candidate))) return candidate;
        }
    }

    private static string Slug(string name)
    {
        var slug = Regex.Replace(name ?? "", "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
        return slug.Length == 0 ? "flow" : slug;
    }

    // ── Data-exchange profiles under a sub-project ────────────────────────────────────────────

    /// <summary>Profile ids stored under {sub}/data-exchange/ (one folder per profile).</summary>
    public IReadOnlyList<string> ListProfiles(string subProjectPath) =>
        DirNames(FullPath(Join(subProjectPath, "data-exchange")));

    /// <summary>All depth-3 sub-project paths ("org/project/sub") under the root, ordinal-ignore-case sorted.</summary>
    public IReadOnlyList<string> ListAllSubProjects()
    {
        var result = new List<string>();
        foreach (var org in ListOrgs())
            foreach (var project in ListProjects(org))
                foreach (var sub in ListSubProjects(org, project))
                    result.Add(Join(org, project, sub));
        return result.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ── Node chain helpers ────────────────────────────────────────────────────────────────────

    /// <summary>Creates the full org/project/sub-project chain plus empty flows/ and data-exchange/ dirs (used by migration).</summary>
    public void EnsureSubProject(string subProjectPath)
    {
        var parts = SplitPath(subProjectPath);
        if (parts.Length != MaxDepth)
            throw new InvalidOperationException($"A sub-project path has exactly {MaxDepth} segments (org/project/sub-project), got '{subProjectPath}'");

        var current = _root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            Directory.CreateDirectory(current);
        }
        Directory.CreateDirectory(Path.Combine(current, "flows"));
        Directory.CreateDirectory(Path.Combine(current, "data-exchange"));
    }

    // ── Path plumbing ─────────────────────────────────────────────────────────────────────────

    /// <summary>Resolves a relative node path to an absolute directory, refusing paths that escape the root.</summary>
    private string FullPath(string? nodePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, (nodePath ?? "").Replace('/', Path.DirectorySeparatorChar)));
        if (!string.Equals(full, _root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Node path '{nodePath}' escapes the workspace root");
        return full;
    }
}
