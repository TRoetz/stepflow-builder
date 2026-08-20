using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.DataExchange;

/// <summary>
/// File-based log of Data Exchange execution results - one JSON artifact per execution under
/// &lt;OutputDirectory&gt;/executions/. Survives restarts and feeds the UI execution monitor.
/// </summary>
public class DataExchangeExecutionLog
{
    private readonly string _directory;

    public DataExchangeExecutionLog(IOptions<DataExchangeOptions> options) =>
        _directory = Path.Combine(options.Value.OutputDirectory, "executions");

    /// <summary>Persists an execution result artifact and returns its id.</summary>
    public string Record(JObject result)
    {
        Directory.CreateDirectory(_directory);
        var id = (string?)result["executionId"] ?? Guid.NewGuid().ToString("N")[..8];
        File.WriteAllText(Path.Combine(_directory, $"{id}.json"), result.ToString(Formatting.Indented));
        return id;
    }

    public JObject? Get(string executionId)
    {
        var file = Path.Combine(_directory, $"{Sanitize(executionId)}.json");
        if (!File.Exists(file)) return null;
        try
        {
            return JObject.Parse(File.ReadAllText(file));
        }
        catch (JsonException)
        {
            return null; // partial write: treat as missing rather than failing the request
        }
    }

    /// <summary>Newest-first list of recent execution artifacts.</summary>
    public JArray List(int limit = 50)
    {
        if (!Directory.Exists(_directory)) return new JArray();

        var items = Directory.GetFiles(_directory, "*.json")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .Take(Math.Max(1, Math.Min(limit, 200)))
            .Select(f =>
            {
                try
                {
                    return JToken.Parse(File.ReadAllText(f));
                }
                catch (JsonException)
                {
                    return null; // skip corrupt artifacts
                }
            })
            .Where(t => t != null);

        var array = new JArray();
        foreach (var item in items) array.Add(item!);
        return array;
    }

    private static string Sanitize(string id) => System.Text.RegularExpressions.Regex.Replace(id, @"[^A-Za-z0-9._-]", "");
}
