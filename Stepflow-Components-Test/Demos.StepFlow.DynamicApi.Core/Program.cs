using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using StepFlow.DynamicApi;
using StepFunctionsApp.StepFunctions;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// STEPFLOW.DYNAMICAPI.CORE DEMO - hosts a dynamic API catalog in-process (the same
// pattern as DynamicApiHost) and self-tests the full request lifecycle over HTTP:
// route matching with path params, bearer auth, body+query merging, EAV GET
// mapping (collection / lookup), and 404/405 semantics.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

Console.WriteLine("+++ StepFlow.DynamicApi.Core demo +++");

// ── 1. The catalog: APIs are data, not code ────────────────────────────────────
var apis = new List<DynamicApiDefinition>
{
    new()
    {
        Id = "customers-api",
        Name = "Customers API",
        Description = "Demo dynamic API over an in-memory EAV row store",
        BasePath = "customers",
        BearerToken = "demo-token",
        IsActive = true,
        IsPublished = true,
        Operations = new List<DynamicApiOperation>
        {
            new() { Method = "GET",  Path = "/",      HandlerType = "eav",  Description = "List customers (full EAV query language)" },
            new() { Method = "GET",  Path = "/{id}",  HandlerType = "eav",  Description = "Get one customer by id" },
            new() { Method = "POST", Path = "/",      HandlerType = "flow", FlowId = "customer-intake", Description = "Run the intake flow with merged input" },
        }
    }
};

// ── 2. In-memory EAV rows backing the "eav" handler (same shape as eav-data) ───
var customers = new List<EavRow>
{
    new() { RowKeyId = "r1", EntityId = 42, EntityType = "customer", Values = JObject.Parse("""{"name":"Alice","region":"EU","amount":300}""") },
    new() { RowKeyId = "r2", EntityId = 7,  EntityType = "customer", Values = JObject.Parse("""{"name":"Bob","region":"US","amount":150}""") },
    new() { RowKeyId = "r3", EntityId = 99, EntityType = "customer", Values = JObject.Parse("""{"name":"Carol","region":"EU","amount":80}""") },
};

// ── 3. Host: catch-all endpoint wired through the matcher (README pattern) ─────
int port = GetFreePort();
var builder = WebApplication.CreateBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();
app.Urls.Add($"http://127.0.0.1:{port}");

const string RoutePrefix = "/api/dynamic";

app.Map("/api/dynamic/{**rest}", async (HttpContext ctx) =>
{
    var method = ctx.Request.Method.ToUpperInvariant();
    var path = ctx.Request.Path.Value ?? "";
    var rest = path.StartsWith(RoutePrefix, StringComparison.OrdinalIgnoreCase) ? path[RoutePrefix.Length..] : path;

    // 1) Match the request against the definitions.
    var matched = DynamicApiMatcher.Match(method, rest, apis);
    if (matched is null)
    {
        var allowed = DynamicApiMatcher.AllowedMethods(rest, apis).OrderBy(m => m).ToList();
        if (allowed.Count > 0) ctx.Response.Headers["Allow"] = string.Join(", ", allowed);
        ctx.Response.StatusCode = allowed.Count > 0 ? StatusCodes.Status405MethodNotAllowed : StatusCodes.Status404NotFound;
        await WriteJson(ctx, new JObject { ["error"] = allowed.Count > 0 ? "Method not allowed" : $"No dynamic API matches {method} {rest}" });
        return;
    }

    // 2) Bearer auth from the definition.
    if (!string.IsNullOrEmpty(matched.Api.BearerToken) &&
        !DynamicApiAuth.CheckBearer(ctx.Request.Headers.Authorization.ToString(), matched.Api.BearerToken!))
    {
        ctx.Response.Headers["WWW-Authenticate"] = "Bearer realm=\"stepflow-dynamic\"";
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await WriteJson(ctx, new JObject { ["error"] = "Invalid or missing bearer token" });
        return;
    }

    // 3) Body + query + path params -> handler input.
    string rawBody = null;
    if (method is "POST" or "PUT" or "PATCH")
        rawBody = await DynamicApiInput.ReadRawBodyAsync(ctx);

    var (body, parseError) = DynamicApiInput.ParseBody(rawBody);
    if (parseError is not null)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await WriteJson(ctx, new JObject { ["error"] = $"Invalid JSON body: {parseError}" });
        return;
    }

    // 4) Dispatch by handler type.
    switch (matched.Operation.HandlerType)
    {
        case "eav":
            var mapping = EavGetMapper.Map(matched.Operation.Path, matched.PathParams, ctx.Request.QueryString.Value);
            await HandleEavAsync(ctx, customers, mapping);
            break;

        case "flow":
            // A real host would hand `input` to the flow engine (StepFlow.Asl).
            var input = DynamicApiInput.MergeInput(body ?? new JObject(), ctx.Request.Query, matched.PathParams);
            await WriteJson(ctx, new JObject
            {
                ["status"] = "Succeeded",
                ["flowId"] = matched.Operation.FlowId,
                ["input"] = input
            });
            break;

        default:
            ctx.Response.StatusCode = StatusCodes.Status501NotImplemented;
            await WriteJson(ctx, new JObject { ["error"] = $"Unknown handler type '{matched.Operation.HandlerType}'" });
            break;
    }
});

await app.StartAsync();
Console.WriteLine($"dynamic API host listening on http://127.0.0.1:{port}/api/dynamic");

// ── 4. Self-test over real HTTP ────────────────────────────────────────────────
var failures = 0;
using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "demo-token");

// (a) no token -> 401
client.DefaultRequestHeaders.Authorization = null; // anonymous request
var r = await client.GetAsync("/api/dynamic/customers/42");
Check(ref failures, "GET /customers/42 without token", r.StatusCode == HttpStatusCode.Unauthorized, $"{(int)r.StatusCode}");
client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "demo-token");

// (b) lookup by path param -> 200 + the row
r = await client.GetAsync("/api/dynamic/customers/42");
var b = await r.Content.ReadAsStringAsync();
Check(ref failures, "GET /customers/42 with token", r.IsSuccessStatusCode && b.Contains("Alice"), $"{(int)r.StatusCode} {b}");

// (c) collection + query language: filter region=EU, sort by -amount, limit 1.
//     NOTE: EavQuery sorts ordinally over the *string* form of values ("80" > "300"),
//     so Carol tops the page; total still counts every match (2).
r = await client.GetAsync("/api/dynamic/customers?region=EU&sort=-amount&limit=1");
b = await r.Content.ReadAsStringAsync();
var c = JObject.Parse(b);
Check(ref failures, "GET /customers?region=EU&sort=-amount&limit=1",
    r.IsSuccessStatusCode && (int)c["total"] == 2 && ((JArray)c["rows"]).Count == 1 && b.Contains("Carol"), $"{(int)r.StatusCode} {b}");

// (d) POST: body + query merged into the flow input
r = await client.PostAsync("/api/dynamic/customers?source=test", new StringContent("""{"name":"Dave"}""", System.Text.Encoding.UTF8, "application/json"));
b = await r.Content.ReadAsStringAsync();
Check(ref failures, "POST /customers (body+query merge)",
    r.IsSuccessStatusCode && b.Contains("\"flowId\":\"customer-intake\"") && b.Contains("Dave") && b.Contains("\"source\":\"test\""),
    $"{(int)r.StatusCode} {b}");

// (e) unknown path -> 404
r = await client.GetAsync("/api/dynamic/nope");
Check(ref failures, "GET /nope", r.StatusCode == HttpStatusCode.NotFound, $"{(int)r.StatusCode}");

// (f) wrong method on a known path -> 405 + Allow header
r = await client.DeleteAsync("/api/dynamic/customers/1");
var allowOk = r.Content.Headers.TryGetValues("Allow", out var allowVals) || r.Headers.TryGetValues("Allow", out allowVals);
Check(ref failures, "DELETE /customers/1 (method not allowed)",
    r.StatusCode == HttpStatusCode.MethodNotAllowed && allowOk, $"{(int)r.StatusCode} Allow={string.Join(",", allowVals ?? Array.Empty<string>())}");

Console.WriteLine();
Console.WriteLine(failures == 0 ? "All dynamic API self-tests passed." : $"{failures} self-test(s) FAILED.");

await app.StopAsync();
return failures;

// ── helpers ────────────────────────────────────────────────────────────────────

static void Check(ref int failures, string label, bool ok, string detail)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}: {detail}");
    if (!ok) failures++;
}

static async Task WriteJson(HttpContext ctx, JObject body)
{
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync(body.ToString(Newtonsoft.Json.Formatting.None), ctx.RequestAborted);
}

/// <summary>eav handler: maps the EAV GET onto the in-memory rows exactly like the engine's row routes.</summary>
static async Task HandleEavAsync(HttpContext ctx, List<EavRow> rows, EavGetMapping mapping)
{
    switch (mapping.Mode)
    {
        case EavGetMode.Lookup:
            var found = EavQuery.Lookup(rows, mapping.Id);
            if (found.Count == 0)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await WriteJson(ctx, new JObject { ["error"] = $"no row with id '{mapping.Id}'" });
                return;
            }
            await WriteJson(ctx, EavQuery.Shape(found[0], null));
            break;

        case EavGetMode.Collection:
        case EavGetMode.EntityFilter:
            var (page, total) = EavQuery.Apply(rows, EavQuery.Parse(QueryFromRaw(mapping.Query)));
            await WriteJson(ctx, new JObject { ["rows"] = new JArray(page), ["total"] = total });
            break;
    }
}

/// <summary>Raw query string -> IQueryCollection (the host-agnostic package never sees ASP.NET types).</summary>
static QueryCollection QueryFromRaw(string raw)
{
    var dict = new Dictionary<string, StringValues>();
    foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        int eq = pair.IndexOf('=');
        if (eq < 0) dict[Uri.UnescapeDataString(pair)] = "";
        else dict[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
    }
    return new QueryCollection(dict);
}

static int GetFreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int p = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return p;
}
