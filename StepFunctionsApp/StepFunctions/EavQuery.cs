using System.Globalization;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// EAV QUERY LANGUAGE - the full GET surface for eav rows, shared by the engine's
// /api/eav/{domain}/rows controller and the in-process dynamic API dispatcher so both
// answer identically:
//  - any non-reserved query key filters rows by field equality (top-level row fields or
//    captured Values keys); multiple values for one key OR, different keys AND.
//    Reserved keys: sort, page, limit, offset, fields. entityId is an ordinary filter key.
//  - sort=capturedAtUtc,-entityId : comma list; '-' prefix descends; unknown keys are no-ops;
//    missing values sort first (empty string).
//  - pagination: limit (default 100), offset, or page (1-based); page+offset together -> ArgumentException.
//  - fields=a,b projects rows to those wire names (top-level camelCase properties or Values keys);
//    rowKeyId is always kept so clients can still address the row for CRUD.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

public static class EavQuery
{
    public const int DefaultLimit = 100;

    /// <summary>Parsed eav GET query: filters (key -> OR values), sort keys, resolved limit/offset, field projection.</summary>
    public sealed record EavQueryOptions(
        IReadOnlyDictionary<string, string[]> Filters,
        IReadOnlyList<(string Key, bool Desc)> SortKeys,
        int Limit,
        int Offset,
        string? Fields);

    /// <summary>Parses the query string; unparseable reserved values fall back to defaults (lenient), page+offset conflict throws.</summary>
    public static EavQueryOptions Parse(IQueryCollection query)
    {
        var filters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var sortKeys = new List<(string Key, bool Desc)>();
        int limit = DefaultLimit;
        int offset = 0;
        bool hasPage = false, hasOffset = false;
        int page = 1;
        string? fields = null;

        foreach (var group in query)
        {
            var values = group.Value.Where(v => !string.IsNullOrWhiteSpace(v)).Cast<string>().ToArray();
            if (values.Length == 0) continue; // empty values carry no constraint (legacy entityId behavior)

            switch (group.Key.ToLowerInvariant())
            {
                case "sort":
                    foreach (var item in values[0].Split(','))
                    {
                        var key = item.Trim();
                        if (key.Length == 0) continue;
                        bool desc = key[0] == '-';
                        if (desc) key = key[1..].Trim();
                        if (key.Length > 0) sortKeys.Add((key, desc));
                    }
                    break;

                case "page":
                    hasPage = true;
                    if (int.TryParse(values[0], out var p)) page = Math.Max(1, p);
                    break;

                case "limit":
                    if (int.TryParse(values[0], out var l) && l > 0) limit = l;
                    break;

                case "offset":
                    hasOffset = true;
                    if (int.TryParse(values[0], out var o)) offset = Math.Max(0, o);
                    break;

                case "fields":
                    fields = values[0];
                    break;

                default:
                    if (!filters.TryGetValue(group.Key, out var bucket))
                        filters.Add(group.Key, bucket = new List<string>());
                    bucket.AddRange(values);
                    break;
            }
        }

        if (hasPage && hasOffset)
            throw new ArgumentException("Use either 'page' or 'offset', not both");

        return new EavQueryOptions(filters.ToDictionary(kv => kv.Key, kv => (string[])kv.Value.ToArray()), sortKeys, limit, hasPage ? (page - 1) * limit : offset, fields);
    }

    /// <summary>Filters, sorts and paginates rows; returns the page of wire-shaped rows plus the post-filter total.</summary>
    public static (JObject[] Rows, int Total) Apply(IEnumerable<EavRow> rows, EavQueryOptions options)
    {
        var filtered = rows.Where(r => options.Filters.All(f => f.Value.Any(v => string.Equals(FieldString(r, f.Key), v, StringComparison.Ordinal)))).ToList();

        IEnumerable<EavRow> sorted = filtered;
        for (int i = options.SortKeys.Count - 1; i >= 0; i--)
        {
            var (key, desc) = options.SortKeys[i];
            sorted = desc
                ? sorted.OrderByDescending(r => FieldString(r, key) ?? "", StringComparer.Ordinal)
                : sorted.OrderBy(r => FieldString(r, key) ?? "", StringComparer.Ordinal);
        }

        var pageList = sorted.Skip(options.Offset).Take(options.Limit).ToList();
        return (pageList.Select(r => Shape(r, options.Fields)).ToArray(), filtered.Count);
    }

    /// <summary>Single-row lookup: exact EntityId match first (ordinal); falls back to RowKeyId when no entity matches.</summary>
    public static IReadOnlyList<EavRow> Lookup(IEnumerable<EavRow> rows, string id)
    {
        var byEntity = rows.Where(r => string.Equals(EntityIdString(r), id, StringComparison.Ordinal)).ToList();
        if (byEntity.Count > 0) return byEntity;
        return rows.Where(r => string.Equals(r.RowKeyId, id, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Wire shape of a row: camelCase properties, Values keys preserved - identical to the MVC controllers' output.</summary>
    public static JObject Shape(EavRow row, string? fields)
    {
        var full = JObject.FromObject(row, WireJson);
        if (string.IsNullOrWhiteSpace(fields)) return full;

        var projected = new JObject();
        foreach (var rawName in fields.Split(','))
        {
            var name = rawName.Trim();
            if (name.Length == 0) continue;

            JToken? token = full[name]; // top-level wire property (camelCase)
            if ((token is null || token.Type == JTokenType.Null) && row.Values.TryGetValue(name, out var value) && value.Type != JTokenType.Null)
                token = value;          // captured values are addressable at the top level under their own name

            if (token is not null && token.Type != JTokenType.Null) projected[name] = token;
        }

        if (!projected.ContainsKey("rowKeyId")) projected["rowKeyId"] = full["rowKeyId"]; // keep the row addressable for CRUD
        return projected;
    }

    /// <summary>Field value as a comparable string: top-level wire fields are case-insensitive, Values keys exact.</summary>
    private static string? FieldString(EavRow row, string key) => key.ToLowerInvariant() switch
    {
        "rowkeyid" => row.RowKeyId,
        "entityid" => EntityIdString(row),
        "entitytype" => row.EntityType,
        "sourcetaskid" => row.SourceTaskId,
        "capturedatutc" => row.CapturedAtUtc.ToString("o", CultureInfo.InvariantCulture),
        _ when row.Values.TryGetValue(key, out var value) && value.Type != JTokenType.Null => value.ToString(),
        _ => null
    };

    private static string? EntityIdString(EavRow row)
    {
        if (row.EntityId is null) return null;
        if (row.EntityId is JToken token && token.Type == JTokenType.Null) return null; // JSON null deserializes as a null JValue
        return row.EntityId.ToString();
    }

    /// <summary>camelCase properties, dictionary keys preserved - identical wire shape to the MVC controllers.</summary>
    private static readonly JsonSerializer WireJson = new() { ContractResolver = new KeyPreservingCamelCaseContractResolver(), Formatting = Formatting.None };
}
