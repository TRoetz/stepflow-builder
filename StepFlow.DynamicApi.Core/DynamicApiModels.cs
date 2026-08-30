namespace StepFlow.DynamicApi;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// DYNAMIC API MODELS - user-defined REST endpoints attached to workspace nodes
// (OU = org / project / sub-project) and optionally to an AttributeDomain. Each
// operation dispatches to one of four handlers: flow, attributeDomain, eav or
// dataExchange. Definitions persist in the shared stepflow_data.db as one row per
// API with its operations serialized into a single JSON column (see SqliteDynamicApiStore).
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

/// <summary>One REST operation of a dynamic API.</summary>
public sealed class DynamicApiOperation
{
    /// <summary>HTTP method: GET, POST, PUT, PATCH or DELETE.</summary>
    public string Method { get; set; } = "GET";

    /// <summary>Path relative to the API's BasePath ("" matches the base path exactly); template params as full "{name}" segments.</summary>
    public string Path { get; set; } = "";

    /// <summary>"flow" | "attributeDomain" | "eav" | "dataExchange".</summary>
    public string HandlerType { get; set; } = "flow";

    /// <summary>Flow id or name (handler: flow).</summary>
    public string? FlowId { get; set; }

    /// <summary>Attribute domain override for this operation (handlers: attributeDomain, eav); falls back to the API-level AttributeDomain.</summary>
    public string? DomainName { get; set; }

    /// <summary>DataExchange profile id (handler: dataExchange).</summary>
    public string? ProfileId { get; set; }

    /// <summary>Free-text shown in the OpenAPI spec and builder UI.</summary>
    public string? Description { get; set; }
}

/// <summary>A user-defined REST API attached to a workspace node (depth 1-3).</summary>
public sealed class DynamicApiDefinition
{
    /// <summary>Stable id: sanitized explicit Id, or slug(Name), uniquified with -2/-3 suffixes.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>Workspace node path "org" | "org/project" | "org/project/sub".</summary>
    public string NodePath { get; set; } = "";

    /// <summary>URL prefix under /api/dynamic, e.g. "/orders"; must start with '/', no trailing '/' unless exactly '/'.</summary>
    public string BasePath { get; set; } = "/";

    /// <summary>Optional AttributeDomain the API is bound to (default domain for attributeDomain/eav operations).</summary>
    public string? AttributeDomain { get; set; }

    /// <summary>When non-empty, requests must carry "Authorization: Bearer &lt;token&gt;".</summary>
    public string? BearerToken { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>When true the API is exposed on external Dynamic API hosts.</summary>
    public bool IsPublished { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<DynamicApiOperation> Operations { get; set; } = new();
}
