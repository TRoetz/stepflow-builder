namespace StepFunctionsApp.Workspace;

/// <summary>
/// Role constants and ranking for workspace ACLs. Roles are ranked low → high:
/// viewer(1) &lt; editor(2) &lt; admin(3) &lt; owner(4). Unknown or blank values normalize to viewer.
/// </summary>
public static class AccessRole
{
    public const string Viewer = "viewer";
    public const string Editor = "editor";
    public const string Admin = "admin";
    public const string Owner = "owner";

    /// <summary>Normalizes a role string (case-insensitive, trimmed). Returns false for unknown values; <paramref name="normalized"/> is then viewer.</summary>
    public static bool TryParse(string? role, out string normalized)
    {
        var value = (role ?? string.Empty).Trim().ToLowerInvariant();
        if (value is Editor or Admin or Owner)
        {
            normalized = value;
            return true;
        }

        normalized = Viewer;
        return false;
    }

    /// <summary>Rank of a role: viewer=1, editor=2, admin=3, owner=4 (unknown → 1).</summary>
    public static int Rank(string? role)
    {
        TryParse(role, out var normalized);
        return normalized switch
        {
            Editor => 2,
            Admin => 3,
            Owner => 4,
            _ => 1
        };
    }

    /// <summary>The higher-ranked of two roles (both normalized).</summary>
    public static string Max(string? a, string? b)
    {
        TryParse(a, out var na);
        TryParse(b, out var nb);
        return Rank(na) >= Rank(nb) ? na : nb;
    }
}

/// <summary>Actions gateable on a workspace node. The rank matrix lives in <see cref="WorkspaceAccessService.MinimumRank"/>.</summary>
public enum WorkspaceAction
{
    View = 1,
    Edit = 2,
    Delete = 3,
    ManageAccess = 4
}

/// <summary>A single ACL entry: a principal (e.g. user name) granted a role on one node.</summary>
public class AccessEntry
{
    public string Principal { get; set; } = "";
    public string Role { get; set; } = AccessRole.Viewer;
}

/// <summary>The local ACL file of a workspace node (access.json).</summary>
public class NodeAcl
{
    public List<AccessEntry> Entries { get; set; } = new();
}

/// <summary>A grant after merging the ACLs along org → project → sub-project. Higher rank wins per principal.</summary>
public class EffectiveGrant
{
    public string Principal { get; set; } = "";
    public string Role { get; set; } = AccessRole.Viewer;
    /// <summary>Node path whose local ACL supplied the winning (highest-rank) entry — lets the UI label inherited vs local grants.</summary>
    public string SourcePath { get; set; } = "";
}

/// <summary>Metadata persisted beside each flow's ASL document (meta.json).</summary>
public class WorkspaceFlowMeta
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ── Tree DTOs (camelCase on the wire via KeyPreservingCamelCaseContractResolver) ────────────────

public class OrgNodeDto
{
    public string Name { get; set; } = "";
    public List<ProjectNodeDto> Projects { get; set; } = new();
}

public class ProjectNodeDto
{
    public string Name { get; set; } = "";
    public List<SubProjectNodeDto> SubProjects { get; set; } = new();
}

public class SubProjectNodeDto
{
    public string Name { get; set; } = "";
    public List<FlowRefDto> Flows { get; set; } = new();
    public List<string> ProfileIds { get; set; } = new();
}

/// <summary>A flow reference inside a sub-project node (id + display name).</summary>
public class FlowRefDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
