using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DynamicApi;

namespace StepFlow.DynamicApi.Host;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// HOST DISPATCHER - external twin of the backend's in-process DynamicApiDispatcher.
// Matching, bearer auth and input merging come from StepFlow.DynamicApi.Core so both
// dispatchers behave identically; every handler executes over HTTP against the
// backend engine (flows -> api/flows/execute-sync/{id}, dataExchange ->
// api/data-exchange/execute, attributeDomain -> api/attribute-domains[/{name}],
// eav -> api/eav/{domain}/rows[/{rowKeyId}]) and passes status + JSON body through.
// Engine 200 responses for flows are mapped by their `status` field exactly like the
// in-process dispatcher; any other engine status/body passes through unchanged, so
// external error semantics track the engine's own REST surface.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

public sealed class HostDynamicApiDispatcher
{
    private const string RoutePrefix = "/api/dynamic";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CatalogService _catalog;
    private readonly ILogger<HostDynamicApiDispatcher> _logger;

    public HostDynamicApiDispatcher(IHttpClientFactory httpClientFactory, CatalogService catalog, ILogger<HostDynamicApiDispatcher>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _catalog = catalog;
        _logger = logger ?? NullLogger<HostDynamicApiDispatcher>.Instance;
    }

    public async Task HandleAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method.ToUpperInvariant();
        var path = context.Request.Path.Value ?? "";
        var rest = path.StartsWith(RoutePrefix, StringComparison.OrdinalIgnoreCase) ? path[RoutePrefix.Length..] : path;

        (int Status, string Body, string? ApiId, string? Handler) result;
        try
        {
            if (!_catalog.IsLoaded)
                result = (StatusCodes.Status503ServiceUnavailable, Error("dynamic API catalog not loaded yet"), null, null);
            else
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
            await context.Response.WriteAsync(result.Body, context.RequestAborted);
        }

        stopwatch.Stop();
        _logger.LogInformation("Dynamic API request: {Method} {Path} api={ApiId} handler={Handler} status={Status} elapsed={ElapsedMs}ms",
            method, rest, result.ApiId ?? "-", result.Handler ?? "-", result.Status, (int)stopwatch.ElapsedMilliseconds);
    }

    private async Task<(int Status, string Body, string? ApiId, string? Handler)> DispatchAsync(HttpContext context, string method, string rest)
    {
        var apis = _catalog.Apis;
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

        if (!string.IsNullOrEmpty(matched.Api.BearerToken) && !DynamicApiAuth.CheckBearer(context.Request.Headers.Authorization.ToString(), matched.Api.BearerToken!))
        {
            context.Response.Headers["WWW-Authenticate"] = "Bearer realm=\"stepflow-dynamic\"";
            return (StatusCodes.Status401Unauthorized, Error("Invalid or missing bearer token"), apiId, handler);
        }

        string? rawBody = null;
        if (method is "POST" or "PUT" or "PATCH")
            rawBody = await DynamicApiInput.ReadRawBodyAsync(context);

        (int status, string body) = handler switch
        {
            "flow" => await HandleFlowAsync(context, matched, rawBody),
            "dataExchange" => await HandleDataExchangeAsync(context, matched, rawBody),
            "attributeDomain" => await HandleAttributeDomainAsync(context, matched, rawBody),
            "eav" => await HandleEavAsync(context, matched, rawBody),
            _ => (StatusCodes.Status500InternalServerError, Error($"Unknown handler type '{handler}'")),
        };
        return (status, body, apiId, handler);
    }

    // ── flow handler: POST {engine}/api/flows/{flowId}/execute-sync ────────────────

    private async Task<(int Status, string Body)> HandleFlowAsync(HttpContext context, MatchedOperation matched, string? rawBody)
    {
        var (body, parseError) = DynamicApiInput.ParseBody(rawBody);
        if (parseError != null) return (StatusCodes.Status400BadRequest, Error(parseError));

        var input = DynamicApiInput.MergeInput(body, context.Request.Query, matched.PathParams);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/flows/execute-sync/{Uri.EscapeDataString(matched.Operation.FlowId!)}")
            { Content = JsonContent(input.ToString(Formatting.None)) };
            using var response = await SendAsync(request, context);
            var rawResponse = await ReadBodyAsync(response, context.RequestAborted);

            // Engine 200 responses are mapped by their status field exactly like the in-process dispatcher.
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var result = ParseJson(rawResponse);
                switch ((string?)result?["status"])
                {
                    case "Succeeded": return (StatusCodes.Status200OK, result?["output"]?.ToString(Formatting.None) ?? "{}");
                    case "Suspended": return (StatusCodes.Status202Accepted, new JObject { ["status"] = "suspended", ["executionId"] = result?["executionId"] }.ToString(Formatting.None));
                    default: // Failed | Aborted | TimedOut
                        return (StatusCodes.Status500InternalServerError, new JObject { ["error"] = result?["errorCode"] ?? "States.Failed", ["message"] = result?["errorMessage"] }.ToString(Formatting.None));
                }
            }

            // Any other engine status/body passes through unchanged.
            return ((int)response.StatusCode, rawResponse);
        }
        catch (Exception ex) when (IsEngineFailure(ex, context))
        {
            return (StatusCodes.Status502BadGateway, Error($"engine unavailable: {ex.Message}"));
        }
    }

    // ── dataExchange handler: POST {engine}/api/data-exchange/execute ───────────────

    private async Task<(int Status, string Body)> HandleDataExchangeAsync(HttpContext context, MatchedOperation matched, string? rawBody)
    {
        var (body, parseError) = DynamicApiInput.ParseBody(rawBody);
        if (parseError != null) return (StatusCodes.Status400BadRequest, Error(parseError));

        var input = DynamicApiInput.MergeInput(body, context.Request.Query, matched.PathParams);
        var payload = new JObject { ["profileId"] = matched.Operation.ProfileId!, ["input"] = input };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/data-exchange/execute") { Content = JsonContent(payload.ToString(Formatting.None)) };
            using var response = await SendAsync(request, context);
            return ((int)response.StatusCode, await ReadBodyAsync(response, context.RequestAborted));
        }
        catch (Exception ex) when (IsEngineFailure(ex, context))
        {
            return (StatusCodes.Status502BadGateway, Error($"engine unavailable: {ex.Message}"));
        }
    }

    // ── attributeDomain handler: /api/attribute-domains[/{name}] on the engine ──────

    private async Task<(int Status, string Body)> HandleAttributeDomainAsync(HttpContext context, MatchedOperation matched, string? rawBody)
    {
        var op = matched.Operation;
        var domainName = !string.IsNullOrWhiteSpace(op.DomainName) ? op.DomainName! : matched.Api.AttributeDomain;
        if (string.IsNullOrWhiteSpace(domainName))
            return (StatusCodes.Status500InternalServerError, Error("Operation has no attribute domain configured"));

        var method = context.Request.Method.ToUpperInvariant();
        if (method is not ("GET" or "POST" or "PUT" or "PATCH"))
            return (StatusCodes.Status501NotImplemented, Error($"Unsupported method {context.Request.Method} for attributeDomain handler"));

        try
        {
            // POST/PUT/PATCH all map to the engine's upsert-by-name collection endpoint.
            using var request = method == "GET"
                ? new HttpRequestMessage(HttpMethod.Get, $"api/attribute-domains/{Uri.EscapeDataString(domainName)}")
                : new HttpRequestMessage(HttpMethod.Post, "api/attribute-domains") { Content = rawBody != null ? JsonContent(rawBody) : null };
            using var response = await SendAsync(request, context);
            return ((int)response.StatusCode, await ReadBodyAsync(response, context.RequestAborted));
        }
        catch (Exception ex) when (IsEngineFailure(ex, context))
        {
            return (StatusCodes.Status502BadGateway, Error($"engine unavailable: {ex.Message}"));
        }
    }

    // ── eav handler: /api/eav/{domain}/rows[/{rowKeyId}] on the engine ───────────────

    private async Task<(int Status, string Body)> HandleEavAsync(HttpContext context, MatchedOperation matched, string? rawBody)
    {
        var op = matched.Operation;
        var domainName = !string.IsNullOrWhiteSpace(op.DomainName) ? op.DomainName! : matched.Api.AttributeDomain;
        if (string.IsNullOrWhiteSpace(domainName))
            return (StatusCodes.Status500InternalServerError, Error("Operation has no attribute domain configured"));

        var method = context.Request.Method.ToUpperInvariant();
        if (method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE"))
            return (StatusCodes.Status501NotImplemented, Error($"Unsupported method {context.Request.Method} for eav handler"));

        string url;
        switch (method)
        {
            case "GET":
            {
                EavGetMapping mapping;
                try
                {
                    mapping = EavGetMapper.Map(matched.Operation.Path, matched.PathParams, context.Request.QueryString.Value);
                }
                catch (ArgumentException ex)
                {
                    return (StatusCodes.Status400BadRequest, Error(ex.Message));
                }

                var route = mapping.Mode == EavGetMode.Lookup ? $"rows/{Uri.EscapeDataString(mapping.Id!)}" : "rows";
                url = $"api/eav/{Uri.EscapeDataString(domainName)}/{route}{(mapping.Query.Length > 0 ? "?" + mapping.Query : "")}";
                break;
            }
            case "POST":
                url = $"api/eav/{Uri.EscapeDataString(domainName)}/rows";
                break;
            default: // PUT | PATCH | DELETE target a single row by path param.
                var rowKeyId = GetPathParam(matched.PathParams, "rowKeyId");
                if (string.IsNullOrWhiteSpace(rowKeyId))
                    return (StatusCodes.Status400BadRequest, Error("This operation requires a {rowKeyId} path parameter"));
                url = $"api/eav/{Uri.EscapeDataString(domainName)}/rows/{Uri.EscapeDataString(rowKeyId)}";
                break;
        }

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (rawBody != null && method is "POST" or "PUT" or "PATCH")
                request.Content = JsonContent(rawBody);
            using var response = await SendAsync(request, context);
            return ((int)response.StatusCode, await ReadBodyAsync(response, context.RequestAborted));
        }
        catch (Exception ex) when (IsEngineFailure(ex, context))
        {
            return (StatusCodes.Status502BadGateway, Error($"engine unavailable: {ex.Message}"));
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpContext context) =>
        _httpClientFactory.CreateClient(EngineOptions.ClientName).SendAsync(request, HttpCompletionOption.ResponseContentRead, context.RequestAborted);

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadAsStringAsync(ct);

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>Parses an engine JSON body; non-JSON payloads are preserved verbatim as a JSON string.</summary>
    private static JToken? ParseJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JToken.Parse(raw); }
        catch (JsonException) { return new JValue(raw); }
    }

    /// <summary>True for engine-side transport failures (connection refused, timeout), not client aborts.</summary>
    private static bool IsEngineFailure(Exception ex, HttpContext context) =>
        ex is HttpRequestException ||
        (ex is TaskCanceledException && !context.RequestAborted.IsCancellationRequested);

    private static string? GetPathParam(JObject pathParams, string name) =>
        pathParams.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Value.ToString();

    /// <summary>camelCase error body - identical shape to the in-process dispatcher.</summary>
    private static string Error(string message) => new JObject { ["error"] = message }.ToString(Formatting.None);
}
