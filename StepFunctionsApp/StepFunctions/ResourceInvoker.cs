using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Web;
using StepFunctionsApp.DataExchange;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // RESOURCE INVOKER & HANDLERS
    // ═══════════════════════════════════════════════════════════════════════════════

    public interface IResourceInvoker
    {
        Task<JToken> InvokeAsync(string resource, JToken input, CancellationToken ct);
    }

    public class CompositeResourceInvoker : IResourceInvoker
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly RuleEngineService _ruleEngine;
        private readonly MicrosoftRulesEngineService _msRulesEngine;
        private readonly DuckDbTransformService _duckDbTransform;
        private readonly IEavEntityProvider _eavProvider;
        private readonly ScriptExecutionService _scriptExecution;
        private readonly Lazy<StepFunctionService> _stepService;
        private readonly DataExchangeExecutor _dataExchange;
        private readonly SshCommandService _sshCommands;
        private readonly FetchRemoteFilesService _fetchFiles;
        private readonly EavRowStore _eavRows;
        private readonly ILogger<CompositeResourceInvoker> _logger;
        private readonly string _callbackBaseUrl = "http://localhost:5000"; // Should come from config

        public CompositeResourceInvoker(
            IHttpClientFactory httpClientFactory,
            RuleEngineService ruleEngine,
            MicrosoftRulesEngineService msRulesEngine,
            DuckDbTransformService duckDbTransform,
            IEavEntityProvider eavProvider,
            ScriptExecutionService scriptExecution,
            Lazy<StepFunctionService> stepService,
            DataExchangeExecutor dataExchange,
            SshCommandService sshCommands,
            FetchRemoteFilesService fetchFiles,
            EavRowStore eavRows,
            ILogger<CompositeResourceInvoker> logger)
        {
            _httpClientFactory = httpClientFactory;
            _ruleEngine = ruleEngine;
            _msRulesEngine = msRulesEngine;
            _duckDbTransform = duckDbTransform;
            _eavProvider = eavProvider;
            _scriptExecution = scriptExecution;
            _stepService = stepService;
            _dataExchange = dataExchange;
            _sshCommands = sshCommands;
            _fetchFiles = fetchFiles;
            _eavRows = eavRows;
            _logger = logger;
        }

        public async Task<JToken> InvokeAsync(string resource, JToken input, CancellationToken ct)
        {
            if (resource.StartsWith("http://") || resource.StartsWith("https://"))
                return await InvokeHttpAsync(resource, input, ct);

            if (resource.StartsWith("rule://"))
                return await HandleRuleAsync(resource, input, ct);

            // Microsoft RulesEngine: rules://<workflowName>
            if (resource.StartsWith("rules://"))
                return await HandleMsRulesAsync(resource, input, ct);

            // DuckDB Transform: transform://<operation>
            if (resource.StartsWith("transform://"))
                return await HandleTransformAsync(resource, input, ct);

            if (resource.StartsWith("ai://"))
                return await HandleAiAsync(resource, input, ct);

            if (resource.StartsWith("flow://"))
                return await HandleFlowAsync(resource, input, ct);

            if (resource.StartsWith("tool://"))
                return await HandleToolAsync(resource, input, ct);

            if (resource.StartsWith("internal://"))
                return await HandleInternalAsync(resource, input, ct);

            // Data Exchange profile pipeline: dataexchange://<profileId>
            if (resource.StartsWith("dataexchange://"))
            {
                var profileId = resource["dataexchange://".Length..].Trim('/');
                _logger.LogInformation("Executing DataExchange profile: {Profile}", profileId);
                return await _dataExchange.ExecuteAsync(profileId, input as JObject ?? new JObject(), ct);
            }

            // Remote SSH command execution: ssh://<hostName> (curated inventory, AI safety check)
            if (resource.StartsWith("ssh://"))
                return await HandleSshAsync(resource, input, ct);

            // Remote file fetch: fetch://<hostName>?proto=scp|sftp|ftp|xcopy (curated inventory)
            if (resource.StartsWith("fetch://"))
                return await HandleFetchAsync(resource, input, ct);

            // Local SQLite query: sql://<connectionString> (file path, "file:" URI, or "Data Source=<path>")
            if (resource.StartsWith("sql://"))
                return await HandleSqlAsync(resource, input, ct);

            // EAV row store CRUD: eav://<entityType> (read/write/update/patch/delete on eav-data/{domain}.json)
            if (resource.StartsWith("eav://"))
                return await HandleEavAsync(resource, input, ct);

            throw new StepEngineException("States.TaskFailed", $"Unknown resource scheme: {resource}");
        }

        private async Task<JToken> HandleToolAsync(string resource, JToken input, CancellationToken ct)
        {
            var toolName = resource["tool://".Length..].Trim('/');
            var url = $"{_callbackBaseUrl}/api/tools/{toolName}/execute";
            return await InvokeHttpAsync(url, input, ct);
        }

        private async Task<JToken> HandleMsRulesAsync(string resource, JToken input, CancellationToken ct)
        {
            var workflowName = resource["rules://".Length..].Trim('/');
            _logger.LogInformation("Executing Microsoft RulesEngine workflow: {Workflow}", workflowName);

            var inputObj = input as JObject ?? new JObject();
            var result = _msRulesEngine.ExecuteWorkflow(workflowName, inputObj);
            return JObject.FromObject(result);
        }

        private async Task<JToken> HandleTransformAsync(string resource, JToken input, CancellationToken ct)
        {
            var operation = resource["transform://".Length..].Trim('/');
            var lowerOp = operation.ToLowerInvariant();

            if (lowerOp == "javascript" || lowerOp == "python" || lowerOp == "powershell" || lowerOp == "csharp" || lowerOp == "shell")
            {
                _logger.LogInformation("Routing scripting execution to ScriptExecutionService. Language: {Language}", operation);
                
                var script = input["script"]?.ToString() ?? input["parameters"]?["script"]?.ToString() ?? "";
                var inputData = input["input_data"] ?? input["parameters"]?["input_data"] ?? input;
                
                return await _scriptExecution.ExecuteScriptAsync(operation, script, inputData, ct);
            }
            if (lowerOp == "jsonata")
            {
                var expression = input["expression"]?.ToString() ?? input["parameters"]?["expression"]?.ToString();
                if (string.IsNullOrWhiteSpace(expression))
                    throw new ArgumentException("transform://jsonata requires an 'expression' parameter");

                // Upstream data arrives via the standard template property ("input_data.$": "$" in ASL Parameters).
                var data = input["input_data"] ?? input["parameters"]?["input_data"] ?? input;
                _logger.LogInformation("Evaluating JSONata expression ({Length} chars)", expression.Length);
                return JsonataProcessor.Evaluate(expression, data);
            }

            _logger.LogDebug("Executing DuckDB transform: {Operation}", operation);

            var inputObj = input as JObject ?? new JObject();
            // Override operation from resource URI if provided
            if (!string.IsNullOrEmpty(operation) && operation != "query")
                inputObj["operation"] = operation;

            var result = _duckDbTransform.ExecuteTransform(inputObj);
            return result;
        }

        private async Task<JToken> InvokeHttpAsync(string url, JToken input, CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient();
            HttpRequestMessage request;

            // Detect structured handler from ASL Parameters mapping
            if (input is JObject obj && obj["__handler"]?.ToString() == "http")
            {
                var methodStr = obj["method"]?.ToString() ?? "POST";
                var method = new HttpMethod(methodStr.ToUpper());
                var body = obj["body"] ?? new JObject();
                var query = obj["query"] as JObject;
                var auth = obj["auth"] as JObject;

                // 1. Construct URL with Query Parameters
                if (query != null && query.HasValues)
                {
                    var uriBuilder = new UriBuilder(url);
                    var q = HttpUtility.ParseQueryString(uriBuilder.Query);
                    foreach (var prop in query.Properties())
                    {
                        q[prop.Name] = prop.Value.ToString();
                    }
                    uriBuilder.Query = q.ToString();
                    url = uriBuilder.ToString();
                }

                request = new HttpRequestMessage(method, url);

                // 2. Add Authentication Headers
                if (auth != null)
                {
                    var authType = auth["type"]?.ToString();
                    var token = auth["token"]?.ToString();
                    if (!string.IsNullOrEmpty(token) && (authType == "Bearer" || authType == "OAuth"))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    }
                }

                // 3. Add JSON Body (except for GET/DELETE)
                if (method != HttpMethod.Get && method != HttpMethod.Delete)
                {
                    request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                }
            }
            else
            {
                // Fallback to legacy behavior: simple POST with the entire input as body
                request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(input.ToString(Formatting.None), Encoding.UTF8, "application/json")
                };
            }

            var response = await client.SendAsync(request, ct);

            // Opt-in envelope ({status, ok, body}): non-2xx responses become data for downstream Choice states instead of failing the run. Mirrors the UI simulated-runner contract.
            var includeStatus = input is JObject incObj && incObj["includeStatus"]?.Value<bool>() == true;

            if (!includeStatus)
            {
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(ct);
                try { return JToken.Parse(content); } catch { return new JObject { ["body"] = content }; }
            }

            var text = await response.Content.ReadAsStringAsync(ct);
            JToken parsedBody;
            try { parsedBody = JToken.Parse(text); } catch { parsedBody = new JValue(text); }
            return new JObject
            {
                ["status"] = (int)response.StatusCode,
                ["ok"] = response.IsSuccessStatusCode,
                ["body"] = parsedBody
            };
        }

        private async Task<JToken> HandleRuleAsync(string resource, JToken input, CancellationToken ct)
        {
            var uri = new Uri(resource);
            var ruleId = uri.Host; // e.g., rule://CheckCredit -> ruleId is CheckCredit

            // Extract EAV query parameter
            var query = HttpUtility.ParseQueryString(uri.Query);
            var eavEntityName = query["eav"];

            Dictionary<string, object> parameters;

            if (!string.IsNullOrEmpty(eavEntityName))
            {
                // 1. Map dynamic JSON to a strict EAV dictionary (registry ∪ attribute domains)
                var entity = _eavProvider.GetEntity(eavEntityName) ?? throw new ArgumentException($"EAV Entity '{eavEntityName}' not found.");
                parameters = EavMapper.Map(entity, input);
            }
            else
            {
                // Fallback to legacy naive object mapping
                parameters = input.ToObject<Dictionary<string, object>>() ?? new();
            }

            // 2. Execute the rule safely
            var result = _ruleEngine.ExecuteRuleById(ruleId, parameters);
            return JObject.FromObject(result);
        }

        private async Task<JToken> HandleAiAsync(string resource, JToken input, CancellationToken ct)
        {
            var url = $"{_callbackBaseUrl}/api/ai/ask";
            var result = await InvokeHttpAsync(url, input, ct);
            
            var isError = result["isError"]?.Value<bool>() ?? false;
            if (isError) throw new StepEngineException("States.TaskFailed", $"AI failed: {result["errorMessage"]}");
            
            return result;
        }

        private async Task<JToken> HandleFlowAsync(string resource, JToken input, CancellationToken ct)
        {
            var flowId = resource["flow://".Length..].Trim('/');
            var execution = await _stepService.Value.ExecuteSyncAsync(flowId, input, ct);
            if (execution.Status == ExecutionStatus.Failed) 
                throw new StepEngineException(execution.ErrorCode ?? "SubFlow.Failed", execution.ErrorMessage ?? "Sub-flow failed");
            return execution.Output;
        }

        private Task<JToken> HandleInternalAsync(string resource, JToken input, CancellationToken ct)
        {
            var path = resource["internal://".Length..];
            return Task.FromResult<JToken>(path switch
            {
                "echo" => input.DeepClone(),
                "engine/status" => JToken.FromObject(_ruleEngine.GetStatus()),
                "rules/status" => JToken.FromObject(_msRulesEngine.GetStatus()),
                "transform/status" => JToken.FromObject(_duckDbTransform.GetStatus()),
                _ => throw new StepEngineException("States.TaskFailed", $"Unknown internal resource: {resource}")
            });
        }

        private async Task<JToken> HandleSshAsync(string resource, JToken input, CancellationToken ct)
        {
            var hostName = resource["ssh://".Length..].Trim('/');
            if (string.IsNullOrWhiteSpace(hostName))
                throw new StepEngineException("Ssh.NoHost", "Resource must be ssh://<hostName>");

            var command = SshCommandService.ResolveCommand(input);
            if (command == null)
                throw new StepEngineException("Ssh.NoCommand", "No command found in input — connect an upstream text/AI node or set a static 'command' parameter");

            var overrideCheck = input is JObject o && (o["override"]?.Value<bool>() ?? false);
            var timeoutSeconds = input is JObject t ? (t["timeoutSeconds"]?.Value<int>() ?? 30) : 30;
            if (timeoutSeconds < 1) timeoutSeconds = 30;

            _logger.LogInformation("SSH executing on {Host}: {Command} (override={Override})", hostName, command, overrideCheck);
            var result = await _sshCommands.ExecuteAsync(hostName, command, overrideCheck, timeoutSeconds, ct);
            return new JObject
            {
                ["host"] = result.HostName,
                ["command"] = result.Command,
                ["exitCode"] = result.ExitCode,
                ["stdout"] = result.Stdout,
                ["stderr"] = result.Stderr,
                ["durationMs"] = (long)result.DurationMs
            };
        }

        private async Task<JToken> HandleFetchAsync(string resource, JToken input, CancellationToken ct)
        {
            var uri = new Uri(resource);
            var hostName = uri.Host; // e.g., fetch://web-01?proto=sftp -> web-01
            if (string.IsNullOrWhiteSpace(hostName))
                throw new StepEngineException("Fetch.NoHost", "Resource must be fetch://<hostName>?proto=scp|sftp|ftp|xcopy");

            // proto query parameter, default scp — same pattern as rule:// in HandleRuleAsync.
            var query = HttpUtility.ParseQueryString(uri.Query);
            var protocol = string.IsNullOrWhiteSpace(query["proto"]) ? "scp" : query["proto"]!;

            var sourcePath = input is JObject o && o["sourcePath"]?.Type == JTokenType.String ? (string?)o["sourcePath"] ?? "" : "";
            var destDir = input is JObject d && d["destDir"]?.Type == JTokenType.String ? (string?)d["destDir"] ?? "" : "";
            var timeoutSeconds = input is JObject t ? (t["timeoutSeconds"]?.Value<int>() ?? 120) : 120;
            if (timeoutSeconds < 1) timeoutSeconds = 120;

            _logger.LogInformation("Fetching files from {Host} via {Proto}: {Source} -> {Dest}", hostName, protocol, sourcePath, destDir);
            var result = await _fetchFiles.FetchAsync(hostName, protocol, sourcePath, destDir, timeoutSeconds, ct);
            return new JObject
            {
                ["host"] = result.HostName,
                ["protocol"] = result.Protocol,
                ["sourcePath"] = result.SourcePath,
                ["destDir"] = result.DestDir,
                ["files"] = JArray.FromObject(result.Files.Select(f => new JObject
                {
                    ["remotePath"] = f.RemotePath,
                    ["localPath"] = f.LocalPath,
                    ["sizeBytes"] = f.SizeBytes
                })),
                ["fileCount"] = result.Files.Count,
                ["durationMs"] = (long)result.DurationMs
            };
        }

        /// <summary>
        /// Executes a SQL statement against a local SQLite database file.
        /// Input: { query (required), connectionString? } — the connection string falls back to the URI path after sql://.
        /// Only local SQLite files are supported (absolute or CWD-relative paths, "file:" URIs, or "Data Source=&lt;path&gt;" forms);
        /// remote connection strings fail with an explicit error rather than a silent no-op.
        /// SELECT/WITH returns { rows: [...], count }; any other statement returns { changes }.
        /// The query runs verbatim — there is no @param binding and no {{node.field}} interpolation (both are UI-only features);
        /// wire dynamic values upstream via JSONata or ". parameters.
        /// </summary>
        private async Task<JToken> HandleSqlAsync(string resource, JToken input, CancellationToken ct)
        {
            var query = input is JObject q && q["query"]?.Type == JTokenType.String ? (string?)q["query"] : null;
            if (string.IsNullOrWhiteSpace(query))
                throw new StepEngineException("States.TaskFailed", "sql:// requires a 'query' parameter");

            var connectionString = input is JObject c && c["connectionString"]?.Type == JTokenType.String ? (string?)c["connectionString"] ?? "" : "";
            if (string.IsNullOrWhiteSpace(connectionString))
                // URI fallback: preserve a leading slash (absolute path) — only trim trailing slashes/whitespace.
                connectionString = resource["sql://".Length..].Trim().TrimEnd('/');
            var dbPath = ResolveSqliteFilePath(connectionString);

            _logger.LogInformation("Executing SQL against {Db}: {Query}", dbPath, query);

            using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = query;

            if (IsSqlQuery(query))
            {
                using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                var rows = new JArray();
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var row = new JObject();
                    for (var i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = SqliteValueToJToken(reader.GetValue(i));
                    rows.Add(row);
                }
                return new JObject { ["rows"] = rows, ["count"] = rows.Count };
            }

            var changes = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return new JObject { ["changes"] = changes };
        }

        /// <summary>True when the statement starts with SELECT or WITH (the only forms that produce a result set here).</summary>
        private static bool IsSqlQuery(string query)
        {
            var i = 0;
            while (i < query.Length && char.IsWhiteSpace(query[i])) i++;
            var start = i;
            while (i < query.Length && char.IsLetterOrDigit(query[i])) i++;
            return query[start..i].ToUpperInvariant() is "SELECT" or "WITH";
        }

        /// <summary>Resolves a sql:// connection string to an existing local SQLite file path.</summary>
        private static string ResolveSqliteFilePath(string connectionString)
        {
            var cs = connectionString.Trim();

            if (cs.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                // URI form: strip query/fragment; "file:///abs" and "file://host/abs" are absolute, anything else is relative.
                var path = cs["file:".Length..];
                var cut = path.IndexOfAny(new[] { '?', '#' });
                if (cut >= 0) path = path[..cut];
                if (path.StartsWith("//")) path = "/" + path[2..];
                cs = path;
            }
            else if (cs.Contains('='))
            {
                // "Data Source=<path>" form — a connection string without that key is remote and unsupported.
                string? dataSource = null;
                foreach (var part in cs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq > 0 && part[..eq].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                        dataSource = part[(eq + 1)..];
                }
                if (dataSource == null)
                    throw new StepEngineException("States.TaskFailed", "sql:// supports local SQLite database files only (remote connection strings are not supported)");
                cs = dataSource;
            }

            var fullPath = Path.GetFullPath(cs);
            if (!File.Exists(fullPath))
                throw new StepEngineException("States.TaskFailed", $"SQLite database file not found: {fullPath}");
            return fullPath;
        }

        /// <summary>Converts a SQLite column value to JSON: DateTime → ISO-8601 string, blob → base64, everything else typed.</summary>
        private static JToken SqliteValueToJToken(object? value) => value switch
        {
            null or DBNull => JValue.CreateNull(),
            bool b => new JValue(b),
            int i => new JValue(i),
            long l => new JValue(l),
            double d => new JValue(d),
            float f => new JValue(f),
            decimal m => new JValue(m),
            DateTime dt => new JValue(dt.ToString("o")),
            byte[] bytes => new JValue(Convert.ToBase64String(bytes)),
            _ => new JValue(value.ToString() ?? string.Empty)
        };

        /// <summary>
        /// CRUD against the file-based EAV row store (eav-data/{entityType}.json).
        /// Input: { operation?, entityType?, values?, rowKeyId? } — entityType falls back to the URI path after eav://.
        /// read (default): returns a bare JArray of all rows' Values objects in append order; input is ignored by design.
        /// write: appends one row per element of values (JObject → single row, scalar wrapped as { value }); output { count }.
        /// update/patch: replaces or merges the Values of the row with rowKeyId; output { updated } / { patched }.
        /// delete: removes the row with rowKeyId; output { removed }.
        /// </summary>
        private Task<JToken> HandleEavAsync(string resource, JToken input, CancellationToken ct)
        {
            var obj = input as JObject ?? new JObject();
            var entityType = (string?)obj["entityType"] ?? resource["eav://".Length..].Trim('/');
            if (string.IsNullOrWhiteSpace(entityType))
                throw new StepEngineException("States.TaskFailed", "eav:// requires an entity type (URI path or 'entityType' parameter)");

            var operation = ((string?)obj["operation"] ?? "read").ToLowerInvariant();
            _logger.LogInformation("EAV {Operation} on domain {Domain}", operation, entityType);

            try
            {
                switch (operation)
                {
                    case "read":
                        return Task.FromResult<JToken>(new JArray(_eavRows.ListRows(entityType).Select(r => (JToken)r.Values.DeepClone())));

                    case "write":
                    {
                        var rows = ResolveEavValues(obj, excludeRowKeyId: false);
                        foreach (var values in rows) _eavRows.AppendRow(entityType, new EavRow { Values = values });
                        return Task.FromResult<JToken>(new JObject { ["count"] = rows.Count });
                    }

                    case "update":
                    case "patch":
                    {
                        var rowKeyId = (string?)obj["rowKeyId"];
                        if (string.IsNullOrWhiteSpace(rowKeyId))
                            throw new StepEngineException("States.TaskFailed", $"eav:// {operation} requires a 'rowKeyId' parameter");
                        var rows = ResolveEavValues(obj, excludeRowKeyId: true);
                        if (rows.Count != 1)
                            throw new StepEngineException("States.TaskFailed", $"eav:// {operation} requires a single 'values' object");
                        var ok = operation == "update"
                            ? _eavRows.UpdateRow(entityType, rowKeyId!, rows[0])
                            : _eavRows.PatchRow(entityType, rowKeyId!, rows[0]);
                        if (!ok) throw new StepEngineException("States.TaskFailed", "row not found");
                        return Task.FromResult<JToken>(new JObject { [operation == "update" ? "updated" : "patched"] = true });
                    }

                    case "delete":
                    {
                        var rowKeyId = (string?)obj["rowKeyId"];
                        if (string.IsNullOrWhiteSpace(rowKeyId))
                            throw new StepEngineException("States.TaskFailed", "eav:// delete requires a 'rowKeyId' parameter");
                        if (!_eavRows.RemoveRow(entityType, rowKeyId!))
                            throw new StepEngineException("States.TaskFailed", "row not found");
                        return Task.FromResult<JToken>(new JObject { ["removed"] = true });
                    }

                    default:
                        throw new StepEngineException("States.TaskFailed", $"eav:// unknown operation '{operation}' (valid: read, write, update, patch, delete)");
                }
            }
            catch (ArgumentException ex)
            {
                // Surface the store's domain-name validation error under the standard task-failure code.
                throw new StepEngineException("States.TaskFailed", ex.Message);
            }
        }

        /// <summary>Extracts the values to persist from an eav:// input: explicit 'values' wins; otherwise the input minus control keys.</summary>
        private static List<JObject> ResolveEavValues(JObject input, bool excludeRowKeyId)
        {
            var values = input["values"];
            if (values == null || values.Type == JTokenType.Null)
            {
                // No explicit 'values': the upstream payload minus control keys is the row data.
                var rest = new JObject();
                foreach (var prop in input.Properties())
                    if (!IsEavControlKey(prop.Name, excludeRowKeyId)) rest[prop.Name] = prop.Value;
                values = rest;
            }

            List<JObject> rows;
            if (values is JArray arr)
                rows = arr.Select(ToEavValues).ToList();
            else if (values is JObject obj && obj.Count > 0)
                rows = new List<JObject> { (JObject)obj.DeepClone() };
            else if (values.Type == JTokenType.Object || values.Type == JTokenType.Array)
                throw new StepEngineException("States.TaskFailed", "eav:// write requires non-empty 'values'");
            else
                rows = new List<JObject> { new JObject { ["value"] = values } };

            if (rows.Count == 0)
                throw new StepEngineException("States.TaskFailed", "eav:// write requires non-empty 'values'");
            return rows;
        }

        private static bool IsEavControlKey(string name, bool excludeRowKeyId) =>
            name is "operation" or "entityType" || (excludeRowKeyId && name == "rowKeyId");

        private static JObject ToEavValues(JToken item) => item switch
        {
            JObject obj => (JObject)obj.DeepClone(),
            _ => new JObject { ["value"] = item }
        };
    }
}
