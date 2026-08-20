namespace StepFunctionsApp.DataExchange;

/// <summary>
/// Configuration for the Data Exchange subsystem (appsettings section "DataExchange").
/// Paths are relative to the content root unless absolute.
/// </summary>
public class DataExchangeOptions
{
    public const string SectionName = "DataExchange";

    /// <summary>Poll interval for the file monitor (Phase 3).</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Directory holding profile JSON documents.</summary>
    public string ProfilesDirectory { get; set; } = "dataexchange/profiles";

    /// <summary>Inbox directory for uploaded customer files, per-profile subfolders.</summary>
    public string InboxDirectory { get; set; } = "dataexchange/inbox";

    /// <summary>Output directory for file-based dispatch targets and execution artifacts.</summary>
    public string OutputDirectory { get; set; } = "dataexchange/output";
}
