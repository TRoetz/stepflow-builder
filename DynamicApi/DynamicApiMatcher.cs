using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.DynamicApi;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// DYNAMIC API MATCHER - pure path matching for /api/dynamic requests. Splits the
// configured (basePath + opPath) and request paths into non-empty '/' segments,
// matches full "{param}" template segments against any literal, and picks the
// candidate with the most literal segments (tie-break: Api.Id ordinal). No I/O here.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

/// <summary>A matched operation plus its extracted path parameters.</summary>
public sealed record MatchedOperation(DynamicApiDefinition Api, DynamicApiOperation Operation, JObject PathParams);

public static class DynamicApiMatcher
{
    private static readonly Regex ParamNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>Collapses duplicate slashes, keeps a single leading '/', strips trailing '/' (except bare "/").</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        var p = path.Replace("\\", "/");
        while (p.Contains("//")) p = p.Replace("//", "/");
        if (!p.StartsWith('/')) p = "/" + p;
        if (p.Length > 1) p = p.TrimEnd('/');
        return p;
    }

    /// <summary>Joins an API base path with an operation path and normalizes the result.</summary>
    public static string JoinPaths(string basePath, string opPath) => Normalize((basePath ?? "/") + (opPath ?? ""));

    /// <summary>Non-empty '/' segments of a normalized path ("/a//b/" → ["a","b"]).</summary>
    public static string[] PathSegments(string path) =>
        Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Finds the best-matching active operation for method+path; null when nothing matches.</summary>
    public static MatchedOperation? Match(string method, string requestPath, IReadOnlyList<DynamicApiDefinition> apis)
    {
        var req = PathSegments(requestPath);
        MatchedOperation? best = null;
        int bestLiterals = -1;

        foreach (var api in apis ?? Array.Empty<DynamicApiDefinition>())
        {
            if (!api.IsActive || api.Operations == null) continue;
            foreach (var op in api.Operations)
            {
                if (op == null || !string.Equals(op.Method, method, StringComparison.OrdinalIgnoreCase)) continue;
                var full = PathSegments(JoinPaths(api.BasePath, op.Path));
                if (full.Length != req.Length) continue;

                var pathParams = new JObject();
                int literals = 0;
                bool ok = true;
                for (int i = 0; i < full.Length; i++)
                {
                    var seg = full[i];
                    if (IsTemplate(seg))
                    {
                        var name = seg[1..^1];
                        if (!ParamNameRegex.IsMatch(name)) { ok = false; break; }
                        pathParams[name] = req[i];
                    }
                    else
                    {
                        if (!string.Equals(seg, req[i], StringComparison.Ordinal)) { ok = false; break; }
                        literals++;
                    }
                }
                if (!ok) continue;

                // Most literal segments wins; tie-break by Api.Id ordinal.
                if (best == null || literals > bestLiterals ||
                    (literals == bestLiterals && string.CompareOrdinal(api.Id, best.Api.Id) < 0))
                {
                    best = new MatchedOperation(api, op, pathParams);
                    bestLiterals = literals;
                }
            }
        }
        return best;
    }

    /// <summary>HTTP methods allowed at the request path across all active APIs (for a 405 Allow header); empty when the path matches nothing.</summary>
    public static IReadOnlySet<string> AllowedMethods(string requestPath, IReadOnlyList<DynamicApiDefinition> apis)
    {
        var req = PathSegments(requestPath);
        var methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var api in apis ?? Array.Empty<DynamicApiDefinition>())
        {
            if (!api.IsActive || api.Operations == null) continue;
            foreach (var op in api.Operations)
            {
                if (op == null || string.IsNullOrWhiteSpace(op.Method)) continue;
                var full = PathSegments(JoinPaths(api.BasePath, op.Path));
                if (StructurallyMatches(full, req)) methods.Add(op.Method.ToUpperInvariant());
            }
        }
        return methods;
    }

    /// <summary>True when a segment is a full "{paramName}" template.</summary>
    public static bool IsTemplate(string segment) =>
        segment.Length > 2 && segment.StartsWith('{') && segment.EndsWith('}');

    private static bool StructurallyMatches(string[] full, string[] req)
    {
        if (full.Length != req.Length) return false;
        for (int i = 0; i < full.Length; i++)
            if (!IsTemplate(full[i]) && !string.Equals(full[i], req[i], StringComparison.Ordinal))
                return false;
        return true;
    }
}
