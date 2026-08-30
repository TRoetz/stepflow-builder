using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFlow.DynamicApi;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// DYNAMIC API REQUEST SUPPORT - shared request helpers used by both dispatchers:
// the in-process backend dispatcher (StepFunctionsApp) and the external Dynamic
// API host. Bearer-token check, raw JSON body reading/parsing and handler-input
// merging. No dependency on either app's services; no I/O beyond the request stream.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

/// <summary>Per-API bearer token enforcement (constant-time compare).</summary>
public static class DynamicApiAuth
{
    /// <summary>True when the Authorization header is exactly "Bearer &lt;expected&gt;" (single space), compared in constant time.</summary>
    public static bool CheckBearer(string? authorizationHeader, string expected)
    {
        var header = authorizationHeader ?? "";
        const string prefix = "Bearer ";
        if (header.Length <= prefix.Length || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var a = Encoding.UTF8.GetBytes(header[prefix.Length..]);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>Request body parsing and handler-input merging (path params &gt; query string &gt; JSON body).</summary>
public static class DynamicApiInput
{
    /// <summary>Parses the raw JSON body; (empty object, null) when there is none.</summary>
    public static (JObject Body, string? Error) ParseBody(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return (new JObject(), null);
        try
        {
            var parsed = JToken.Parse(rawBody);
            return parsed is JObject obj ? (obj, null) : (null!, "Body must be a valid JSON object");
        }
        catch (JsonException ex)
        {
            return (null!, $"Body is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>Reads the request body when it carries application/json; null otherwise.</summary>
    public static async Task<string?> ReadRawBodyAsync(HttpContext context)
    {
        var contentType = context.Request.ContentType ?? "";
        if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)) return null;
        using var reader = new StreamReader(context.Request.Body);
        var text = await reader.ReadToEndAsync();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Merges handler input: body properties, then query-string values (as strings), then path params (path params win).</summary>
    public static JObject MergeInput(JObject body, IQueryCollection query, JObject pathParams)
    {
        var input = new JObject();
        foreach (var prop in body.Properties()) input[prop.Name] = prop.Value;
        foreach (var kv in query)
            foreach (var value in kv.Value) input[kv.Key] = value;
        foreach (var prop in pathParams.Properties()) input[prop.Name] = prop.Value;
        return input;
    }
}
