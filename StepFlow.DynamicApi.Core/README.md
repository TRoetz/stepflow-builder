# StepFlow.DynamicApi.Core

Core of the Dynamic API feature: user-defined REST APIs that are declared as data (JSON definitions attached to a workspace node) and dispatched at runtime. This package contains everything that is host-agnostic — route matching, request parsing/auth helpers, models, and EAV GET mapping. The actual hosting (dispatcher + persistence + flow resolution) lives in the consuming app; see `DynamicApiHost/` in this repo for a minimal reference host.

## Package

| | |
|---|---|
| **Id** | `StepFlow.DynamicApi.Core` |
| **Version** | 1.0.0 |
| **Target framework** | net10.0 (uses the ASP.NET Core shared framework for `HttpContext`/`IQueryCollection`) |

## Dependencies

No project references (leaf package). NuGet:

- `Newtonsoft.Json` 13.0.4

## What's inside

| Type | Role |
|---|---|
| `DynamicApiDefinition` | The API declaration: id, name, workspace node path, URL `BasePath`, optional bound attribute domain, bearer token, active/external flags, and its operations. |
| `DynamicApiOperation` | One route on an API: method + relative path (with `{param}` segments) and a handler — `"flow"` (invoke a flow), `"attributeDomain"`, `"eav"`, or `"dataExchange"` (run a profile). |
| `DynamicApiMatcher` | Static routing engine: `Normalize`, `JoinPaths`, `PathSegments`, and `Match(method, requestPath, apis)` → best-matching active operation (`MatchedOperation`) with extracted path params. |
| `DynamicApiAuth` / `DynamicApiInput` | Request helpers: bearer-token check, raw body reading/parsing, and merging body + query + path params into the handler input document. |
| `EavGetMapper` | Maps an EAV GET operation (path template + query string) onto the shared EAV query options — keeps dynamic-API EAV reads consistent with the builder's REST endpoints. |

All types are in namespace `StepFlow.DynamicApi`.

## Usage

```xml
<PackageReference Include="StepFlow.DynamicApi.Core" Version="1.0.0" />
```

The pattern every host follows (full working example: `DynamicApiHost/Program.cs`):

```csharp
using StepFlow.DynamicApi;

// 1) Load your API definitions from wherever you persist them (file, DB, …)
IReadOnlyList<DynamicApiDefinition> apis = /* load */;

app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/api/dynamic"), dynamicApp =>
{
    dynamicApp.Map("/{**rest}", async ctx =>
    {
        // 2) Match the request against the definitions
        var match = DynamicApiMatcher.Match(
            ctx.Request.Method.ToUpperInvariant(),
            ctx.Request.Path.Value!, apis);
        if (match is null) { ctx.Response.StatusCode = 404; return; }
        // match: record(Api, Operation, PathParams /* JObject */)

        // 3) Auth + input assembly
        var api = match.Api;
        if (!string.IsNullOrEmpty(api.BearerToken) &&
            !DynamicApiAuth.CheckBearer(ctx.Request.Headers.Authorization, api.BearerToken))
        { ctx.Response.StatusCode = 401; return; }

        string? rawBody = await DynamicApiInput.ReadRawBodyAsync(ctx);
        var (body, parseError) = DynamicApiInput.ParseBody(rawBody);
        if (parseError is not null) { ctx.Response.StatusCode = 400; return; }

        var input = DynamicApiInput.MergeInput(body ?? new JObject(),
            ctx.Request.Query, match.PathParams);

        // 4) Dispatch to your handler for match.Operation.HandlerType:
        //    "flow" → run the flow engine with `input`
        //    "eav" / "attributeDomain" → read rows (EavGetMapper.Map helps here)
        //    "dataExchange" → run the profile via StepFlow.DataExchange
        await ctx.Response.WriteAsync(input.ToString());   // ← replace with real dispatch
    });
});
```

## Notes

- **Definitions are data**: an API is a JSON document, not code — that's what makes them publishable per workspace node and exportable in solution packages.
- Bearer tokens travel inside the definition; treat definitions as sensitive when sharing environments.
- The builder app keeps its own hosting layer (`StepFunctionsApp/DynamicApi/`) because it resolves `flow` handlers against its flow engine and persists definitions in SQLite — this package is deliberately free of those concerns so you can host dynamic APIs anywhere (the standalone `DynamicApiHost` exposes them on port 5002).
