namespace StepFunctionsApp.Workspace;

/// <summary>
/// Permission-check seam for workspace nodes. Today it only evaluates stored ACL metadata — when
/// real authentication lands, middleware calls CanAct with the authenticated principal and nothing
/// in storage or the API surface changes.
/// </summary>
public interface IWorkspaceAccessService
{
    /// <summary>Merged grants along org → project → sub-project (higher rank wins per principal).</summary>
    IReadOnlyList<EffectiveGrant> GetEffectiveGrants(string nodePath);

    /// <summary>True when the principal's effective role on the node covers the action. Unknown/absent principals are denied.</summary>
    bool CanAct(string? principal, string nodePath, WorkspaceAction action);
}

public class WorkspaceAccessService : IWorkspaceAccessService
{
    private readonly WorkspaceStore _store;

    public WorkspaceAccessService(WorkspaceStore store) => _store = store;

    /// <summary>Action → minimum role rank matrix (single source of truth): viewer→View, editor→Edit, admin→Delete, owner→ManageAccess.</summary>
    public static int MinimumRank(WorkspaceAction action) => action switch
    {
        WorkspaceAction.View => AccessRole.Rank(AccessRole.Viewer),
        WorkspaceAction.Edit => AccessRole.Rank(AccessRole.Editor),
        WorkspaceAction.Delete => AccessRole.Rank(AccessRole.Admin),
        WorkspaceAction.ManageAccess => AccessRole.Rank(AccessRole.Owner),
        _ => int.MaxValue
    };

    public IReadOnlyList<EffectiveGrant> GetEffectiveGrants(string nodePath) => _store.GetEffectiveGrants(nodePath);

    public bool CanAct(string? principal, string nodePath, WorkspaceAction action)
    {
        if (string.IsNullOrWhiteSpace(principal)) return false;

        var grant = _store.GetEffectiveGrants(nodePath)
            .FirstOrDefault(g => string.Equals(g.Principal, principal.Trim(), StringComparison.OrdinalIgnoreCase));
        return grant != null && AccessRole.Rank(grant.Role) >= MinimumRank(action);
    }
}
