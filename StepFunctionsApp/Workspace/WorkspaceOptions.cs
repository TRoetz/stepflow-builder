namespace StepFunctionsApp.Workspace;

/// <summary>
/// Configuration for the workspace hierarchy (appsettings section "Workspace").
/// Paths are relative to the content root unless absolute.
/// </summary>
public class WorkspaceOptions
{
    public const string SectionName = "Workspace";

    /// <summary>Root directory of the org → project → sub-project tree.</summary>
    public string RootDirectory { get; set; } = "workspace-data";

    /// <summary>Name used for each level of the default node chain created by migration.</summary>
    public string DefaultNodeName { get; set; } = "Default";
}
