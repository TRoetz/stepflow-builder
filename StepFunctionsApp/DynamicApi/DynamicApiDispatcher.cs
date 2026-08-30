using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DynamicApi;
using StepFunctionsApp.StepFunctions;
using StepFunctionsApp.Workspace;

namespace StepFunctionsApp.DynamicApi;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// DYNAMIC API DISPATCHER - handles every request under /api/dynamic. Matches the
// route against active APIs, enforces the per-API bearer token, builds the handler
// input (path params > query string > JSON body) and dispatches to one of four
// handlers: flow | attributeDomain | eav | dataExchange. Every request is logged
// at Information with method, path, api id, handler, status and elapsed ms.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

public class DynamicApiDispatcher
{
    private const string RoutePrefix = "/api/dynamic";

    private readonly IDynamicApiStore _store;
    private readonly StepFunctionService _stepService;
    private readonly IAttributeDomainStore _domainStore;
    private readonly EavRowStore _eavRows;
    private readonly DataExchange.DataExchangeExecutor _dataExchange;
    private readonly WorkspaceStore _workspace;
    private readonly FlowResolver _flowResolver;
    private readonly DynamicApiHostingOptions _hosting;
    private readonly ILogger<DynamicApiDispatcher> _logger;

    public DynamicApiDispatcher(
        IDynamicApiStore store,
        StepFunctionService stepService,
        IAttributeDomainStore domainStore,
        EavRowStore eavRows,
        DataExchange.DataExchangeExecutor dataExchange,
        WorkspaceStore workspace,
        FlowResolver flowResolver,
        DynamicApiHostingOptions hosting,
        ILogger<DynamicApiDispatcher>? logger = null)
    {
        _store = store;
        _stepService = stepService;
        _domainStore = domainStore;
        _eavRows = eavRows;
        _dataExchange = dataExchange;
        _workspace = workspace;
        _flowResolver = flowResolver;
        _hosting = hosting;
        _logger = logger ?? NullLogger<DynamicApiDispatcher>.Instance;
    }

    public async Task HandleAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method.ToUpperInvariant();
        var path = context.Request.Path.Value ?? "";
        var rest = path.StartsWith(RoutePrefix, StringComparison.OrdinalIgnoreCase) ? path[RoutePrefix.Length..] : path;

        (int Status, JToken Body, string? ApiId, string? Handler) result;
        try
        {
            result = await DispatchAsync(context, method, rest);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Dynamic API request failed: {Method} {Path}", method, rest);
            result = (StatusCodes.Status500InternalServerError, Error(ex.Message), null, null);
        }

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = result.Status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(result.Body.ToString(Formatting.None));
        }

        stopwatch.Stop();
        _logger.LogInformation("Dynamic API request: {Method} {Path} api={ApiId} handler={Handler} status={Status} elapsed={ElapsedMs}ms",
            method, rest, result.ApiId ?? "-", result.Handler ?? "-", result.Status, (int)stopwatch.ElapsedMilliseconds);
    }

    private async Task<(int Status, JToken Body, string? ApiId, string? Handler)> DispatchAsync(HttpContext context, string method, string rest)
    {
        // Scoped port → only this node's subtree; management/unknown port → all APIs.
        var binding = _hosting.BindingForPort(context.Connection.LocalPort);
        var apis = _store.GetAll(binding?.NodePath);
        var matched = DynamicApiMatcher.Match(method, rest, apis);

        if (matched == null)
        {
            var allowed = DynamicApiMatcher.AllowedMethods(rest, apis).OrderBy(m => m).ToList();
            if (allowed.Count > 0) context.Response.Headers["Allow"] = string.Join(", ", allowed);
            return allowed.Count > 0
                ? (StatusCodes.Status405MethodNotAllowed, Error("Method not allowed"), null, null)
                : (StatusCodes.Status404NotFound, Error($"No dynamic API matches {method} {rest}"), null, null);
        }

        var apiId = matched.Api.Id;
        var handler = matched.Operation.HandlerType;

        // Per-API bearer token: exactly "Bearer <token>" (single space), constant-time compare.
        if (!string.IsNullOrEmpty(matched.Api.BearerToken) && !DynamicApiAuth.CheckBearer(context.Request.Headers.Authorization.ToString(), matched.Api.BearerToken!))
        {
            context.Response.Headers["WWW-Authenticate"] = "Bearer realm=\"stepflow-dynamic\"";
            return (StatusCodes.Status401Unauthorized, Error("Invalid or missing bearer token"), apiId, handler);
        }

        // Read the raw body once for methods that may carry one.
        string? rawBody = null;
        if (method is "POST" or "PUT" or "PATCH")
            rawBody = await DynamicApiInput.ReadRawBodyAsync(context);

        (int status, JToken body) = handler switch
        {
            "flow" => await HandleFlowAsync(context, matched, rawBody),
            "dataExchange" => await HandleDataExchangeAsync(context, matched, rawBody),
            "attributeDomain" => HandleAttributeDomain(context, matched, rawBody),
            "eav" => HandleEav(context, matched, rawBody),
            _ => (StatusCodes.Status500InternalServerError, Error($"Unknown handler type '{handler}'")),
        };
        return (status, body, apiId, handler);
    }

    // ── flow handler ────────────────────────────────────────────────────────────────

    private async Task<(int Status, JToken Body)> HandleFlowAsync(HttpContext context, MatchedOperation matched, string? rawBody)
    {
        var (body, parseError) = DynamicApiInput.ParseBody(rawBody);
        if (parseError != null) return (StatusCodes.Status400BadRequest, Error(parseError));

        var input = DynamicApiInput.MergeInput(body, context.Request.Query, matched.PathParams);
        var def = _flowResolver.ResolveFlow(matched.Operation.FlowId!);
        if (def == null) return (StatusCodes.Status404NotFound, Error($"Flow '{matched.Operation.FlowId}' not found"));

        try
        {
            var execution = await _stepService.ExecuteSyncAsync(matched.Operation.FlowId!, input, context.RequestAborted);
            switch (execution.Status)
            {
                case ExecutionStatus.Succeeded:
                    return (StatusCodes.Status200OK, execution.Output ?? new JObject());
                case ExecutionStatus.Suspended:
                    return (StatusCodes.Status202Accepted, new JObject { ["status"] = "suspended", ["executionId"] = execution.ExecutionId });
                default: // Failed | Aborted | TimedOut
                    return (StatusCodes.Status500InternalServerError, new JObject { ["error"] = execution.ErrorCode ?? "States.Failed", ["message"] = execution.ErrorMessage });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (StatusCodes.Status500InternalServerError, Error($"Flow '{matched.Operation.FlowId}' failed to start: {ex.Message}"));
        }
    }


    // ── dataExchange handler ────────────────────────────────────────────────────────

    private async Task<(int Status, JToken Body)> HandleDataExchangeAsync(HttpContext context, MatchedOperation matched, string? rawBody)
    {
        var (body, parseError) = DynamicApiInput.ParseBody(rawBody);
        if (parseError != null) return (StatusCodes.Status400BadRequest, Error(parseError));

        var input = DynamicApiInput.MergeInput(body, context.Request.Query, matched.PathParams);
        try
        {
            var result = await _dataExchange.ExecuteAsync(matched.Operation.ProfileId!, input, context.RequestAborted);
            return (StatusCodes.Status200OK, result ?? new JObject());
        }
        catch (KeyNotFoundException)
        {
            return (StatusCodes.Status404NotFound, Error($"Profile '{matched.Operation.ProfileId}' not found"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (StatusCodes.Status500InternalServerError, Error(ex.Message));
        }
    }

    // ── attributeDomain handler ─────────────────────────────────────────────────────

    private (int Status, JToken Body) HandleAttributeDomain(HttpContext context, MatchedOperation matched, string? rawBody)
    {
        var op = matched.Operation;
        var domainName = !string.IsNullOrWhiteSpace(op.DomainName) ? op.DomainName! : matched.Api.AttributeDomain;
        if (string.IsNullOrWhiteSpace(domainName))
            return (StatusCodes.Status500InternalServerError, Error("Operation has no attribute domain configured"));

        switch (context.Request.Method.ToUpperInvariant())
        {
            case "GET":
                var (domain, schema) = _domainStore.GetByName(domainName);
                if (domain == null) return (StatusCodes.Status404NotFound, Error($"Attribute domain '{domainName}' not found"));
                // Same wire shape as GET /api/attribute-domains items.
                return (StatusCodes.Status200OK, JObject.FromObject(AttributeDomainMapping.FromPoco(domain, schema), WireJson));

            case "POST":
            case "PUT":
            case "PATCH":
            var (body, parseError) = DynamicApiInput.ParseBody(rawBody);
                if (parseError != null) return (StatusCodes.Status400BadRequest, Error(parseError));
                var entry = body.ToObject<AttributeDomainEntry>(WireJson) ?? new AttributeDomainEntry();
                if (entry.AttributeDomain == null) entry.AttributeDomain = new AttributeDomainData();
                if (string.IsNullOrWhiteSpace(entry.AttributeDomain.AttributeDomainName))
                    entry.AttributeDomain.AttributeDomainName = domainName; // empty name adopts the effective one
                else if (!string.Equals(entry.AttributeDomain.AttributeDomainName, domainName, StringComparison.OrdinalIgnoreCase))
                    return (StatusCodes.Status400BadRequest, Error("Body domain name does not match operation target"));

                var (pocoDomain, pocoSchema) = AttributeDomainMapping.ToPoco(entry);
                _domainStore.Save(pocoDomain, pocoSchema);
                return (StatusCodes.Status200OK, new JObject { ["status"] = "saved", ["domainName"] = domainName });

            default:
                return (StatusCodes.Status501NotImplemented, Error($"Unsupported method {context.Request.Method} for attributeDomain handler"));
        }
    }

    // ── eav handler ─────────────────────────────────────────────────────────────────

    private (int Status, JToken Body) HandleEav(HttpContext context, MatchedOperation matched, string? rawBody)
    {
        var op = matched.Operation;
        var domainName = !string.IsNullOrWhiteSpace(op.DomainName) ? op.DomainName! : matched.Api.AttributeDomain;
        if (string.IsNullOrWhiteSpace(domainName))
            return (StatusCodes.Status500InternalServerError, Error("Operation has no attribute domain configured"));

        switch (context.Request.Method.ToUpperInvariant())
        {
            case "GET":
                var query = context.Request.Query;
                string? entityId = query["entityId"].ToString();
                int limit = ParseInt(query["limit"], 100);
                int offset = ParseInt(query["offset"], 0);
                var rows = _eavRows.ListRows(domainName).ToList();
                if (!string.IsNullOrWhiteSpace(entityId))
                    rows = rows.Where(r => string.Equals(r.EntityId?.ToString(), entityId, StringComparison.Ordinal)).ToList();
                var total = rows.Count;
                var page = rows.Skip(Math.Max(0, offset)).Take(limit <= 0 ? 100 : limit).Select(r => JObject.FromObject(r, WireJson));
                return (StatusCodes.Status200OK, new JObject { ["rows"] = new JArray(page), ["count"] = total });

            case "POST":
                var (body, parseError) = DynamicApiInput.ParseBody(rawBody);
                if (parseError != null) return (StatusCodes.Status400BadRequest, Error(parseError));
                // Top-level entityId/entityType become row fields; everything else is captured values.
                var row = new EavRow();
                foreach (var prop in body.Properties())
                {
                    switch (prop.Name.ToLowerInvariant())
                    {
                        case "entityid": row.EntityId = prop.Value.Type == JTokenType.Null ? null : prop.Value; break;
                        case "entitytype": row.EntityType = prop.Value.ToString(); break;
                        default: row.Values[prop.Name] = prop.Value; break;
                    }
                }
                _eavRows.AppendRow(domainName, row); // assigns RowKeyId when empty
                return (StatusCodes.Status201Created, JObject.FromObject(row, WireJson));

            case "PUT":
            case "PATCH":
                var (patchBody, patchError) = DynamicApiInput.ParseBody(rawBody);
                if (patchError != null) return (StatusCodes.Status400BadRequest, Error(patchError));
                var rowKeyId = GetPathParam(matched.PathParams, "rowKeyId");
                if (string.IsNullOrWhiteSpace(rowKeyId))
                    return (StatusCodes.Status400BadRequest, Error("This operation requires a {rowKeyId} path parameter"));
                bool ok = context.Request.Method.ToUpperInvariant() == "PUT"
                    ? _eavRows.UpdateRow(domainName, rowKeyId!, patchBody)
                    : _eavRows.PatchRow(domainName, rowKeyId!, patchBody);
                if (!ok) return (StatusCodes.Status404NotFound, Error($"Row '{rowKeyId}' not found"));
                var updated = _eavRows.ListRows(domainName).FirstOrDefault(r => string.Equals(r.RowKeyId, rowKeyId, StringComparison.OrdinalIgnoreCase));
                return (StatusCodes.Status200OK, JObject.FromObject(updated!, WireJson));

            case "DELETE":
                var delKey = GetPathParam(matched.PathParams, "rowKeyId");
                if (string.IsNullOrWhiteSpace(delKey))
                    return (StatusCodes.Status400BadRequest, Error("This operation requires a {rowKeyId} path parameter"));
                if (!_eavRows.RemoveRow(domainName, delKey!))
                    return (StatusCodes.Status404NotFound, Error($"Row '{delKey}' not found"));
                return (StatusCodes.Status200OK, new JObject { ["status"] = "deleted", ["rowKeyId"] = delKey });

            default:
                return (StatusCodes.Status501NotImplemented, Error($"Unsupported method {context.Request.Method} for eav handler"));
        }
    }

    // ── shared helpers ──────────────────────────────────────────────────────────────

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var v) ? v : fallback;

    private static string? GetPathParam(JObject pathParams, string name) =>
        pathParams.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Value.ToString();

    private static JToken Error(string message) => new JObject { ["error"] = message };

    /// <summary>camelCase properties, dictionary keys preserved - identical wire shape to the MVC controllers.</summary>
    private static readonly JsonSerializer WireJson = new() { ContractResolver = new KeyPreservingCamelCaseContractResolver(), Formatting = Formatting.None };
}
