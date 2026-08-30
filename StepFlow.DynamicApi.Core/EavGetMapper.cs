using Newtonsoft.Json.Linq;

namespace StepFlow.DynamicApi;

/// <summary>How an eav-handler GET request maps onto the engine's row routes.</summary>
public enum EavGetMode
{
    /// <summary>No path parameters - collection route with the full query language applied verbatim.</summary>
    Collection,

    /// <summary>A single identity parameter on an empty or one-segment op path ({id}) - single-row lookup by EntityId, falling back to RowKeyId.</summary>
    Lookup,

    /// <summary>A single identity parameter on a multi-segment op path ({id}/comments) - collection filtered by entityId = the parameter value.</summary>
    EntityFilter
}

/// <summary>The engine route an eav GET request must hit: mode + lookup id (Lookup only) + effective query string without a leading '?'.
/// Query is empty when nothing was sent; EntityFilter always carries the merged entityId pair.</summary>
public readonly record struct EavGetMapping(EavGetMode Mode, string? Id, string Query);

/// <summary>
/// Maps an eav-handler GET request (op path template + matched path parameters + raw query string) to the engine route it must hit.
/// Pure string logic shared by the in-process dispatcher and external dynamic API hosts so both surfaces behave identically:
///  - no path params -> Collection, raw query verbatim;
///  - one param on an empty or one-segment op path ({id}) -> Lookup rows/{value}, query passed through (the engine honors only 'fields');
///  - one param on a multi-segment op path ({id}/comments) -> EntityFilter: collection with entityId={value} merged into the query.
///    A client-sent entityId equal to the parameter value is deduped; a different value conflicts (ArgumentException).
/// PathParams values arrive percent-encoded from DynamicApiMatcher.Match and are decoded here. Two or more path params are invalid for eav GET ops
/// (rejected at save time); Map throws anyway so a hand-built API cannot misbehave silently.
/// </summary>
public static class EavGetMapper
{
    public static EavGetMapping Map(string opPath, JObject pathParams, string? rawQuery)
    {
        var query = Normalize(rawQuery);

        if (pathParams.Count == 0)
            return new EavGetMapping(EavGetMode.Collection, null, query);

        if (pathParams.Count > 1)
            throw new ArgumentException("eav GET operations support at most one path parameter");

        var id = Uri.UnescapeDataString(pathParams.Properties().Single().Value.ToString());
        var opSegments = DynamicApiMatcher.PathSegments(opPath ?? "");
        return opSegments.Length <= 1
            ? new EavGetMapping(EavGetMode.Lookup, id, query)
            : new EavGetMapping(EavGetMode.EntityFilter, id, MergeEntityId(query, id));
    }

    /// <summary>Merges entityId={value} into the query: a same-value pair is deduped; a different value conflicts.</summary>
    private static string MergeEntityId(string query, string value)
    {
        var kept = new List<string>();
        foreach (var pair in SplitPairs(query))
        {
            int eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
            if (!string.Equals(key, "entityId", StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(pair);
                continue;
            }

            var existing = Uri.UnescapeDataString(eq < 0 ? "" : pair[(eq + 1)..]);
            if (existing != value)
                throw new ArgumentException($"Conflicting entityId: path parameter is '{value}' but the query string sends '{existing}'");
        }

        kept.Add("entityId=" + Uri.EscapeDataString(value));
        return string.Join("&", kept);
    }

    private static IEnumerable<string> SplitPairs(string query)
    {
        foreach (var pair in query.Split('&'))
            if (pair.Length > 0) yield return pair;
    }

    private static string Normalize(string? rawQuery) =>
        rawQuery is { Length: > 0 } q ? (q[0] == '?' ? q[1..] : q) : "";
}
