namespace StepFlow.DynamicApi.Host;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// ENGINE OPTIONS - backend engine connection settings (appsettings.json "Engine")
// plus sealed catalog sync options ("Sync"). The named "engine" HttpClient is
// configured from these in Program.cs; all engine requests use relative URIs.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

/// <summary>Backend engine connection settings (appsettings.json "Engine").</summary>
public sealed class EngineOptions
{
    public const string SectionName = "Engine";

    /// <summary>Name of the preconfigured HttpClient used for all engine calls.</summary>
    public const string ClientName = "engine";

    /// <summary>Base URL of the backend API (must be absolute).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5001";

    /// <summary>Per-request timeout for engine calls (seconds).</summary>
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>Catalog sync settings (appsettings.json "Sync").</summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>Polling interval for the published-API catalog (seconds).</summary>
    public int IntervalSeconds { get; set; } = 30;
}
