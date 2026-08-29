namespace StepFunctionsApp.DynamicApi;

/// <summary>One Kestrel endpoint bound to a workspace node subtree.</summary>
public sealed class DynamicApiEndpointBinding
{
    public int Port { get; set; }
    /// <summary>Workspace node path "org" | "org/project" | "org/project/sub". The port serves APIs on this node and everything nested under it.</summary>
    public string NodePath { get; set; } = "";
}

/// <summary>"DynamicApi" section of appsettings.json: which ports the host binds and what each one is scoped to.</summary>
public sealed class DynamicApiHostingOptions
{
    public const string SectionName = "DynamicApi";

    /// <summary>Full management surface (builder UI, controllers, MCP, all dynamic APIs). Default 5001 keeps today's behavior.</summary>
    public int ManagementPort { get; set; } = 5001;

    /// <summary>"localhost" (default), "*", "0.0.0.0", or an IP literal. Every endpoint binds here.</summary>
    public string ListenAddress { get; set; } = "localhost";

    /// <summary>Per-business-unit/project endpoints: one port each, scoped to the node subtree.</summary>
    public List<DynamicApiEndpointBinding> Endpoints { get; set; } = new();

    /// <summary>Binding for a local connection port, or null (management/unknown port → unscoped).</summary>
    public DynamicApiEndpointBinding? BindingForPort(int localPort) =>
        Endpoints.FirstOrDefault(e => e.Port == localPort);

    /// <summary>Fails fast on invalid config: out-of-range ports, duplicate ports, bad node paths. Throws InvalidOperationException.</summary>
    public void Validate()
    {
        if (ManagementPort is < 1 or > 65535)
            throw new InvalidOperationException($"DynamicApi:ManagementPort must be 1-65535, got {ManagementPort}");

        var seen = new Dictionary<int, string> { [ManagementPort] = "management port" };
        foreach (var ep in Endpoints)
        {
            if (ep.Port is < 1 or > 65535)
                throw new InvalidOperationException($"DynamicApi:Endpoints has an out-of-range port {ep.Port}");
            var path = (ep.NodePath ?? "").Trim('/');
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length is < 1 or > 3 || segments.Any(s => s is "." or ".."))
                throw new InvalidOperationException($"DynamicApi:Endpoints port {ep.Port} needs a NodePath of 'org', 'org/project' or 'org/project/sub', got '{ep.NodePath}'");
            ep.NodePath = path; // normalized for store prefix matching (SqliteDynamicApiStore.GetAll trims '/')
            if (!seen.TryAdd(ep.Port, $"endpoint '{path}'"))
                throw new InvalidOperationException($"Dynamic API port {ep.Port} is bound more than once ({seen[ep.Port]} and endpoint '{path}')");
        }
    }
}
