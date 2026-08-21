using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.DataSource;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.StepFunctions;
using Action = StepFlow.DataModel.Entities.DataSource.Action;

namespace StepFunctionsApp.DataExchange;
/// <summary>
/// Executes a Data Exchange profile pipeline: ingests customer data (file or inline rows),
/// then runs the profile's stages - validation/calculation rules, schema mapping to the internal
/// format, lookup enrichment and downstream dispatch (API or file). The original source file is
/// never modified; every stage works on in-memory row objects.
/// </summary>
public class DataExchangeExecutor
{
    /// <summary>{FieldName} tokens - resolved against row values for rule expressions, URLs and templates.</summary>
    private static readonly Regex FieldToken = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private readonly DataExchangeProfileStore _profiles;
    private readonly DuckDbTransformService _duckDb;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DataExchangeExecutor> _logger;

    public DataExchangeExecutor(
        DataExchangeProfileStore profiles,
        DuckDbTransformService duckDb,
        IHttpClientFactory httpClientFactory,
        ILogger<DataExchangeExecutor> logger)
    {
        _profiles = profiles;
        _duckDb = duckDb;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Resolves a profile by id (or name) and executes it. Entry point for the dataexchange:// scheme.</summary>
    public Task<JObject> ExecuteAsync(string profileId, JObject? input, CancellationToken ct = default)
    {
        var profile = _profiles.Get(profileId)
            ?? throw new KeyNotFoundException($"DataExchange profile '{profileId}' not found");

        return ExecuteProfileAsync(profile, input ?? new JObject(), ct);
    }

    public async Task<JObject> ExecuteProfileAsync(DataExchangeProfile profile, JObject input, CancellationToken ct = default)
    {
        var executionId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("DataExchange execution {Execution} started for profile '{Profile}'", executionId, ProfileName(profile));

        var (rows, source) = await IngestAsync(profile, input, executionId, ct);
        int rowsIn = rows.Count;
        var rejected = new JArray();
        var stageResults = new JArray();
        var dispatched = new JArray();
        var universe = BuildAttributeUniverse(profile);

        if (profile.Pipeline != null)
            await ApplyStagesAsync(rows, profile.Pipeline, universe, executionId, rejected, stageResults, dispatched, ct);

        _logger.LogInformation("DataExchange execution {Execution} finished: {In} in / {Out} out / {Rejected} rejected",
            executionId, rowsIn, rows.Count, rejected.Count);

        return new JObject
        {
            ["success"] = true,
            ["executionId"] = executionId,
            ["profileId"] = DataExchangeProfileStore.ResolveId(profile),
            ["source"] = source,
            ["rowsIn"] = rowsIn,
            ["rowsOut"] = rows.Count,
            ["rejectedCount"] = rejected.Count,
            ["rejected"] = rejected,
            ["enrichedRows"] = rows,
            ["stages"] = stageResults,
            ["dispatched"] = dispatched
        };
    }

    // ------------------------------------------------------------------ stages

    private async Task ApplyStagesAsync(
        JArray rows, Pipeline pipeline, Dictionary<int, string> universe, string executionId,
        JArray rejected, JArray stageResults, JArray dispatched, CancellationToken ct)
    {
        foreach (var stage in pipeline.PipelineStages.Where(s => s != null).OrderBy(s => s.ExecutionOrder))
        {
            var stageResult = new JObject
            {
                ["pipeline"] = pipeline.PipelineName,
                ["stageType"] = stage.StageType.ToString(),
                ["order"] = stage.ExecutionOrder,
                ["actions"] = new JArray()
            };

            foreach (var psa in stage.PipelineStageActions.Where(a => a != null).OrderBy(a => a.ExecutionOrder))
            {
                var action = psa.Action;
                if (action == null) continue;

                var messages = new JArray();
                var actionResult = new JObject
                {
                    ["name"] = action.ActionName,
                    ["type"] = action.Type.ToString(),
                    ["messages"] = messages
                };

                switch (action.Type)
                {
                    case ActionType.Logic:
                        RunRules(rows, action, universe, rejected, executionId, messages);
                        break;
                    case ActionType.Transformation:
                        ApplySchemaMap(rows, action, universe, messages);
                        break;
                    case ActionType.EnrichmentLookup:
                        await EnrichRowsAsync(rows, action, universe, ct, messages);
                        break;
                    case ActionType.Dispatch:
                        await DispatchRowsAsync(rows, action, executionId, dispatched, messages, ct);
                        break;
                    case ActionType.ExecutePipeline:
                        foreach (var sub in action.SubPipelines.Where(s => s != null && s.SubPipeline != null))
                            await ApplyStagesAsync(rows, sub.SubPipeline, universe, executionId, rejected, stageResults, dispatched, ct);
                        break;
                    default:
                        messages.Add($"unsupported action type {action.Type}");
                        break;
                }

                ((JArray)stageResult["actions"]).Add(actionResult);
            }

            stageResults.Add(stageResult);
        }
    }

    // ------------------------------------------------------------------ ingestion

    private async Task<(JArray rows, string source)> IngestAsync(DataExchangeProfile profile, JObject input, string executionId, CancellationToken ct)
    {
        if (input["rows"] is JArray explicitRows && explicitRows.Count > 0)
            return (ToRowObjects(explicitRows), "input.rows");

        var mediumConfig = ParseMediumConfiguration(profile.DataSource);

        // Database source - run the configured query and materialize the result set.
        if (profile.DataSource?.MediumType == DataSourceMediumType.Database)
        {
            var connectionString = mediumConfig?["connectionString"]?.ToString();
            var query = mediumConfig?["query"]?.ToString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Database source requires MediumConfigurationJson.connectionString.");
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Database source requires MediumConfigurationJson.query.");

            using var connection = new SqlConnection(connectionString!);
            await connection.OpenAsync(ct);
            using var command = new SqlCommand(query!, connection) { CommandTimeout = 120 };
            using var adapter = new SqlDataAdapter(command);
            var resultTable = new DataTable();
            adapter.Fill(resultTable);
            return (DataTableToRows(resultTable), "database");
        }
        var filePath = input["filePath"]?.ToString() ?? mediumConfig?["filePath"]?.ToString();
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("No 'rows' in input and no file path configured (input.filePath or DataSource.MediumConfigurationJson.filePath).");

        if (!File.Exists(filePath!))
            throw new FileNotFoundException($"DataExchange source file not found: {filePath}", filePath);

        var table = $"dx_{executionId}_src";
        switch (Path.GetExtension(filePath!).ToLowerInvariant())
        {
            case ".csv":
                _duckDb.LoadCsvFile(table, filePath!);
                return (TableToRows(_duckDb.ExecuteQuery($"SELECT * FROM \"{table}\"")), $"file:{filePath}");

            case ".json":
            {
                var parsed = JToken.Parse(await File.ReadAllTextAsync(filePath!, ct));
                var arr = parsed as JArray ?? (parsed is JObject obj && obj["rows"] is JArray rowsProp ? rowsProp : new JArray(parsed));
                return (ToRowObjects(arr), $"file:{filePath}");
            }

            case ".xml":
            {
                var doc = XDocument.Parse(await File.ReadAllTextAsync(filePath!, ct));
                var xmlRows = new JArray();
                foreach (var el in doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
                    xmlRows.Add(XElementToJObject(el));
                return (xmlRows, $"file:{filePath}");
            }

            default:
                throw new NotSupportedException($"Unsupported DataExchange source extension '{Path.GetExtension(filePath)}' (supported: .csv, .json, .xml).");
        }
    }

    private static JArray ToRowObjects(JArray tokens) =>
        new(tokens.OfType<JObject>().Select(o => new JObject(o.Properties().Select(p => new JProperty(p.Name, p.Value)))));

    private static JArray TableToRows(List<Dictionary<string, object>> table)
    {
        var rows = new JArray();
        foreach (var d in table)
        {
            var row = new JObject();
            foreach (var kv in d)
                row[kv.Key] = ToJToken(kv.Value);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Converts a materialized result set into row objects, mapping DBNull to null.</summary>
    public static JArray DataTableToRows(DataTable table)
    {
        var rows = new JArray();
        foreach (DataRow dr in table.Rows)
        {
            var row = new JObject();
            foreach (DataColumn col in table.Columns)
                row[col.ColumnName] = dr[col] is DBNull ? null : ToJToken(dr[col]);
            rows.Add(row);
        }
        return rows;
    }

    private static JToken? ParseMediumConfiguration(DataSource? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource?.MediumConfigurationJson)) return null;
        try { return JObject.Parse(dataSource.MediumConfigurationJson!); }
        catch (JsonException) { return null; }
    }

    private static JObject XElementToJObject(XElement element)
    {
        var obj = new JObject();
        foreach (var attr in element.Attributes())
            obj[attr.Name.LocalName] = attr.Value;
        foreach (var child in element.Elements())
        {
            if (child.HasElements || child.HasAttributes)
                obj[child.Name.LocalName] = XElementToJObject(child);
            else
                obj[child.Name.LocalName] = child.Value;
        }
        return obj;
    }

    // ------------------------------------------------------------------ rules (Logic stage)

    private void RunRules(JArray rows, Action action, Dictionary<int, string> universe, JArray rejected, string executionId, JArray messages)
    {
        var rules = (action.ActionRules ?? new List<ActionRule>()).Where(r => r != null && r.Rule != null).ToList();
        if (rules.Count == 0 || rows.Count == 0) return;

        var table = $"dx_{executionId}_work";
        LoadWorkTable(rows, table);

        // Validation rules - a row is rejected when any validation fails.
        var rejectIdx = new Dictionary<int, string>();
        foreach (var rule in rules.Where(r => r.Rule!.Type == RuleType.Validation))
        {
            var sql = $"SELECT _dx_idx AS idx, CASE WHEN ({RenderExpression(rule.Rule!.Expression)}) THEN 1 ELSE 0 END AS pass FROM \"{table}\"";
            foreach (var r in _duckDb.ExecuteQuery(sql))
            {
                int idx = Convert.ToInt32(r["idx"]);
                if (Convert.ToInt64(r["pass"]) != 1 && !rejectIdx.ContainsKey(idx))
                    rejectIdx[idx] = string.IsNullOrWhiteSpace(rule.Rule.DefaultFailureMessage)
                        ? $"Rule '{rule.Rule.RuleName}' failed"
                        : rule.Rule.DefaultFailureMessage!;
            }
        }

        foreach (var kv in rejectIdx.OrderBy(k => k.Key))
        {
            var row = rows[kv.Key];
            rejected.Add(new JObject { ["rowIndex"] = kv.Key, ["row"] = row, ["reason"] = kv.Value });
            rows.Remove(row);
        }

        if (rows.Count == 0) return;
        LoadWorkTable(rows, table); // reload so calculations see the surviving set only

        // Calculation / Selection rules - write derived values into target attributes.
        foreach (var rule in rules.Where(r => r.Rule!.Type != RuleType.Validation))
        {
            var target = ResolveRuleTarget(rule, universe);
            if (target == null)
            {
                messages.Add($"rule '{rule.Rule!.RuleName}': no resolvable output target; skipped");
                continue;
            }

            var sql = $"SELECT _dx_idx AS idx, ({RenderExpression(rule.Rule.Expression)}) AS val FROM \"{table}\"";
            foreach (var r in _duckDb.ExecuteQuery(sql))
            {
                int idx = Convert.ToInt32(r["idx"]);
                ApplyMerge((JObject)rows[idx], target!, ToJToken(r["val"]), rule.OutputMergeStrategy);
            }
        }
    }

    private static string? ResolveRuleTarget(ActionRule rule, Dictionary<int, string> universe)
    {
        if (!string.IsNullOrWhiteSpace(rule.OutputTargetAttributeName)) return rule.OutputTargetAttributeName;
        if (rule.OutputTargetAttributeId != null && universe.TryGetValue(rule.OutputTargetAttributeId.Value, out var name)) return name;
        return null;
    }

    private void LoadWorkTable(JArray rows, string table)
    {
        var payload = new JArray();
        for (int i = 0; i < rows.Count; i++)
        {
            var copy = new JObject(((JObject)rows[i]).Properties().Select(p => new JProperty(p.Name, p.Value)));
            copy["_dx_idx"] = i;
            payload.Add(copy);
        }

        _duckDb.LoadJsonData(table, payload);
    }

    /// <summary>Rewrites {Field} tokens into quoted DuckDB identifiers so expressions reference row columns.</summary>
    private static string RenderExpression(string expression) =>
        FieldToken.Replace(expression, m => QuoteIdentifier(m.Groups[1].Value.Trim()));

    private static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    // ------------------------------------------------------------------ transformation (schema mapping)

    private void ApplySchemaMap(JArray rows, Action action, Dictionary<int, string> universe, JArray messages)
    {
        var map = action.SchemaMap;
        if (map?.AttributeMappings == null || !map.AttributeMappings.Any())
        {
            if (map == null) messages.Add("no schema map configured");
            return;
        }

        foreach (var m in map.AttributeMappings.Where(m => m != null))
        {
            var target = m.TargetAttribute?.AttributeName ?? ResolveById(m.TargetAttributeId, universe);
            if (string.IsNullOrWhiteSpace(target))
            {
                messages.Add("mapping without resolvable target skipped");
                continue;
            }

            var sourceNames = (m.SourceAttributes ?? new List<EntityAttribute>())
                .Where(s => s != null)
                .Select(s => s.AttributeName!)
                .ToList();

            for (int i = 0; i < rows.Count; i++)
            {
                var rawRow = rows[i];
                JObject row = rawRow is JObject j ? j : throw new InvalidOperationException($"row[{i}] type={rawRow.Type} value={rawRow.ToString(Formatting.None)}");
                var values = sourceNames.Select(n => row[n]).ToList();

                object? value;
                string? note = null;
                switch (m.TransformType)
                {
                    case TransformType.DirectCopy:
                        value = ToPlain(values.FirstOrDefault(v => v != null));
                        break;
                    case TransformType.Combine:
                        value = string.Join(m.Parameters?.GetValueOrDefault("separator") ?? " ",
                            values.Where(v => v != null).Select(ToPlainString));
                        break;
                    case TransformType.Multiply:
                    {
                        var nums = values.Where(v => v != null)
                            .Select(ToPlain)
                            .Where(o => o != null && double.TryParse(o.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                            .Select(o => double.Parse(o!.ToString()!, CultureInfo.InvariantCulture))
                            .ToList();
                        value = nums.Count > 0 ? nums.Aggregate((a, b) => a * b) : null;
                        break;
                    }
                    case TransformType.SetDefault:
                        value = m.Parameters?.GetValueOrDefault("defaultValue");
                        break;
                    case TransformType.Trim:
                        value = ToPlainString(values.FirstOrDefault(v => v != null))?.Trim();
                        break;
                    case TransformType.ToUpper:
                        value = ToPlainString(values.FirstOrDefault(v => v != null))?.ToUpperInvariant();
                        break;
                    case TransformType.FormatDate:
                    {
                        var raw = ToPlainString(values.FirstOrDefault(v => v != null));
                        if (raw == null) value = null;
                        else if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                            value = dt.ToString(m.Parameters?.GetValueOrDefault("format") ?? "yyyy-MM-dd", CultureInfo.InvariantCulture);
                        else { value = raw; note = $"unparseable date '{raw}' kept as-is"; }
                        break;
                    }
                    default:
                        messages.Add($"unsupported transform type {m.TransformType}");
                        continue;
                }

                if (note != null) messages.Add($"{action.ActionName}: row {i}: {note}");
                ApplyMerge(row, target!, ToJToken(value), m.MergeStrategy);
            }
        }
    }

    // ------------------------------------------------------------------ enrichment (lookups)

    private async Task EnrichRowsAsync(JArray rows, Action action, Dictionary<int, string> universe, CancellationToken ct, JArray messages)
    {
        var lookup = action.Lookup;
        if (lookup == null || string.IsNullOrWhiteSpace(lookup.LookupEndpoint))
        {
            messages.Add("no lookup endpoint configured");
            return;
        }

        var mappingJson = ParseJObject(action.LookupResultMappingJson);
        var cache = new Dictionary<string, JToken?>();

        foreach (var row in rows)
        {
            var renderedUrl = RenderUrlTokens(lookup.LookupEndpoint!, (JObject)row);
            var rawTemplate = string.IsNullOrWhiteSpace(lookup.QueryOrBodyTemplate) ? null : lookup.QueryOrBodyTemplate;
            var method = (action.Parameters?.GetValueOrDefault("Method") ?? (rawTemplate != null ? "POST" : "GET")).ToUpperInvariant();
            // Query strings need percent-encoded values; POST bodies keep raw values.
            var renderedTemplate = rawTemplate == null ? null : (method == "GET" ? RenderUrlTokens(rawTemplate, (JObject)row) : RenderTokens(rawTemplate, (JObject)row));

            var cacheKey = $"{method} {renderedUrl} {(renderedTemplate ?? "")}";
            if (!cache.TryGetValue(cacheKey, out JToken? response))
            {
                try
                {
                    string url;
                    string? body = null;
                    if (method == "GET")
                        url = renderedTemplate != null ? AppendQuery(renderedUrl, renderedTemplate) : renderedUrl;
                    else
                    {
                        url = renderedUrl;
                        body = renderedTemplate;
                    }

                    response = await LookupRequestAsync(url, body, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    messages.Add($"lookup failed for row: {ex.Message}");
                    continue;
                }

                cache[cacheKey] = response;
            }

            if (response == null) continue;
            ApplyLookupResult((JObject)row, response, lookup.ValueFieldToReturn, mappingJson, action.OutputParameterName, universe, messages);
        }
    }

    private void ApplyLookupResult(JObject row, JToken response, string? valueFieldToReturn, JObject? mappingJson, string? outputParameterName, Dictionary<int, string> universe, JArray messages)
    {
        JToken? selected = null;
        if (!string.IsNullOrWhiteSpace(valueFieldToReturn))
            selected = SelectPath(response, valueFieldToReturn!);

        var sourceObject = (selected as JObject) ?? (response as JObject);

        if (mappingJson != null && sourceObject != null)
        {
            foreach (var prop in mappingJson.Properties())
            {
                var cfg = prop.Value as JObject;
                var target = cfg?["AttributeName"]?.ToString() ?? (cfg?["TargetAttributeId"] is { Type: JTokenType.Integer } id && universe.TryGetValue((int)id, out var byName) ? byName : null);
                if (target == null || sourceObject[prop.Name] is not { } fieldToken) continue;

                ApplyMerge(row, target, Clone(fieldToken), ParseStrategy(cfg?["MergeStrategy"]));
            }
        }
        else if (selected != null && !string.IsNullOrWhiteSpace(outputParameterName))
        {
            ApplyMerge(row, outputParameterName!, Clone(selected), MergeStrategy.AddNewOnly);
        }
    }

    private async Task<JToken?> LookupRequestAsync(string url, string? body, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();

        if (body == null)
        {
            var text = await client.GetStringAsync(url, ct);
            return TryParse(text);
        }

        using var content = new StringContent(body ?? "", Encoding.UTF8, "application/json");
        var response = await client.PostAsync(url, content, ct);
        var text2 = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("DataExchange lookup {Method} {Url} returned {(int)response.StatusCode}", body == null ? "GET" : "POST", url, (int)response.StatusCode);
            return null;
        }

        return TryParse(text2);
    }

    private static JToken? TryParse(string text)
    {
        try { return JToken.Parse(text); }
        catch (JsonException) { return string.IsNullOrWhiteSpace(text) ? null : new JValue(text); }
    }

    /// <summary>Resolves {Field} tokens against row values; missing fields render as empty strings.</summary>
    private static string RenderTokens(string template, JObject row) =>
        FieldToken.Replace(template, m => ToPlainString(row[m.Groups[1].Value.Trim()]) ?? "");

    /// <summary>Resolves {Field} tokens against row values for URLs; substituted values are percent-encoded.</summary>
    private static string RenderUrlTokens(string template, JObject row) =>
        FieldToken.Replace(template, m => Uri.EscapeDataString(ToPlainString(row[m.Groups[1].Value.Trim()]) ?? ""));

    private static string AppendQuery(string url, string query) => url + (url.Contains('?') ? "&" : "?") + query;

    /// <summary>Walks a dot path ("a.b.c", optional leading $) into a JToken.</summary>
    private static JToken? SelectPath(JToken token, string path)
    {
        var current = token;
        foreach (var segment in path.TrimStart('$').Split('.'))
            current = current[segment];

        return current;
    }

    // ------------------------------------------------------------------ dispatch

    private async Task DispatchRowsAsync(JArray rows, Action action, string executionId, JArray dispatched, JArray messages, CancellationToken ct)
    {
        var url = action.Endpoint?.ActionEndpointURL ?? action.Parameters?.GetValueOrDefault("Url");
        if (string.IsNullOrWhiteSpace(url))
        {
            messages.Add("no dispatch endpoint configured");
            return;
        }

        // Optional subset filter - each dispatch can target a filtered or the full dataset.
        JArray subset = rows;
        var filter = action.Parameters?.GetValueOrDefault("Filter");
        if (!string.IsNullOrWhiteSpace(filter) && rows.Count > 0)
        {
            var table = $"dx_{executionId}_work";
            LoadWorkTable(rows, table);
            var sql = $"SELECT _dx_idx AS idx FROM \"{table}\" WHERE ({RenderExpression(filter!)})";
            var keep = new HashSet<int>(_duckDb.ExecuteQuery(sql).Select(r => Convert.ToInt32(r["idx"])));
            subset = new JArray();
            for (int i = 0; i < rows.Count; i++)
                if (keep.Contains(i)) subset.Add(rows[i]);
        }

        var format = action.Parameters?.GetValueOrDefault("OutputFormat")?.ToLowerInvariant();
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            WriteFileTarget(url["file://".Length..], subset, format);
            dispatched.Add(new JObject { ["endpoint"] = url, ["method"] = "FILE", ["rows"] = subset.Count, ["ok"] = subset.Count, ["failed"] = 0 });
            messages.Add($"wrote {subset.Count} rows to {url}");
            return;
        }

        if (format is "csv" or "json" && !url.Contains("://"))
        {
            WriteFileTarget(url!, subset, format);
            dispatched.Add(new JObject { ["endpoint"] = url, ["method"] = "FILE", ["rows"] = subset.Count, ["ok"] = subset.Count, ["failed"] = 0 });
            messages.Add($"wrote {subset.Count} rows to {url}");
            return;
        }

        var method = (action.Parameters?.GetValueOrDefault("Method") ?? "POST").ToUpperInvariant();
        var batch = string.Equals(action.Parameters?.GetValueOrDefault("Batch"), "true", StringComparison.OrdinalIgnoreCase);
        int ok = 0, failed = 0;
        var statuses = new SortedSet<int>();
        var client = _httpClientFactory.CreateClient();

        IEnumerable<string> bodies = batch ? new[] { subset.ToString(Formatting.None) } : subset.Cast<JObject>().Select(o => o.ToString(Formatting.None));

        foreach (var body in bodies)
        {
            try
            {
                HttpResponseMessage response;
                if (method == "GET")
                    response = await client.GetAsync(url!, ct);
                else
                {
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    response = await client.PostAsync(url!, content, ct);
                }

                statuses.Add((int)response.StatusCode);
                if (response.IsSuccessStatusCode) ok++; else failed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _logger.LogWarning(ex, "DataExchange dispatch request to {Url} failed", url);
            }
        }

        dispatched.Add(new JObject
        {
            ["endpoint"] = url,
            ["method"] = method,
            ["rows"] = subset.Count,
            ["ok"] = ok,
            ["failed"] = failed,
            ["statuses"] = new JArray(statuses)
        });

        if (failed > 0) messages.Add($"{action.ActionName}: {failed}/{subset.Count} dispatch requests failed");
    }

    private void WriteFileTarget(string path, JArray rows, string? format)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var asCsv = (format ?? (ext == ".csv" ? "csv" : null)) is "csv";

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        if (asCsv)
            File.WriteAllText(path, ToCsv(rows), new UTF8Encoding(false));
        else
            File.WriteAllText(path, rows.ToString(Formatting.Indented), new UTF8Encoding(false));
    }

    private static string ToCsv(JArray rows)
    {
        var header = new List<string>();
        foreach (var row in rows.OfType<JObject>())
            foreach (var prop in row.Properties())
                if (!header.Contains(prop.Name)) header.Add(prop.Name);

        var lines = new List<string> { string.Join(",", header.Select(EscapeCsv)) };
        foreach (var row in rows.OfType<JObject>())
            lines.Add(string.Join(",", header.Select(h => EscapeCsv(ToPlainString(row[h])))));

        return string.Join("\n", lines) + "\n";
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    // ------------------------------------------------------------------ helpers

    private static Dictionary<int, string> BuildAttributeUniverse(DataExchangeProfile profile)
    {
        var map = new Dictionary<int, string>();

        void Add(AttributeDomain? domain)
        {
            if (domain?.Attributes == null) return;
            foreach (var a in domain.Attributes.Where(a => a != null))
                if (!map.ContainsKey(a.EntityAttributeId))
                    map[a.EntityAttributeId] = a.AttributeName!;
        }

        Add(profile.DataSource?.ImportSchema);
        if (profile.Pipeline != null)
            foreach (var stage in profile.Pipeline.PipelineStages.Where(s => s != null))
                foreach (var psa in stage.PipelineStageActions.Where(a => a != null && a.Action != null))
                {
                    Add(psa.Action.InputSchema);
                    Add(psa.Action.ActionSchema);
                }

        return map;
    }

    private static string? ResolveById(int? attributeId, Dictionary<int, string> universe) =>
        attributeId != null && universe.TryGetValue(attributeId.Value, out var name) ? name : null;

    private static void ApplyMerge(JObject row, string target, JToken value, MergeStrategy strategy)
    {
        var existing = row[target];
        switch (strategy)
        {
            case MergeStrategy.AddNewOnly:
                if (existing == null || existing.Type == JTokenType.Null) row[target] = value;
                break;
            case MergeStrategy.OverwriteExisting:
                row[target] = value;
                break;
            case MergeStrategy.AppendValue:
                var ex = ToPlainString(existing);
                row[target] = string.IsNullOrEmpty(ex) ? value : (JToken)(ex + ", " + ToPlainString(value));
                break;
        }
    }

    private static JObject? ParseJObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static MergeStrategy ParseStrategy(JToken? token) =>
        Enum.TryParse<MergeStrategy>(token?.ToString(), ignoreCase: true, out var s) ? s : MergeStrategy.AddNewOnly;

    private static JToken Clone(JToken token) => (JToken)token.DeepClone();

    private static object? ToPlain(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        if (token is JValue v) return v.Value;
        return token.ToString(Formatting.None);
    }

    private static string? ToPlainString(JToken? token) => ToPlain(token)?.ToString();

    private static JToken ToJToken(object? value) =>
        value == null ? JValue.CreateNull() : JToken.FromObject(value);

    private static string ProfileName(DataExchangeProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DataExchangeProfileName) ? "(unnamed)" : profile.DataExchangeProfileName!;
}
