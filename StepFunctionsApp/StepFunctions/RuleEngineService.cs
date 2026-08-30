using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // RULE ENGINE SERVICE
    // ═══════════════════════════════════════════════════════════════════════════════

    public class RuleEngineService : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly HashSet<string> _loadedTables = new();
        private readonly Dictionary<string, RuleDefinition> _rules = new();
        private readonly Dictionary<int, DomainInfo> _domains = new();
        private readonly object _lock = new();
        private readonly ILogger<RuleEngineService> _logger;

        public RuleEngineService(ILogger<RuleEngineService> logger)
        {
            _logger = logger;
            _connection = new SqliteConnection("DataSource=:memory:;Cache=Shared");
            _connection.Open();

            _connection.CreateFunction("REGEXP",
                (string pattern, string input) => Regex.IsMatch(input ?? "", pattern ?? ""));

            _connection.CreateFunction("DATEDIFF",
                (string unit, string startStr, string endStr) =>
                {
                    if (string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr)) return 0;
                    if (!DateTime.TryParse(startStr, out var start) || !DateTime.TryParse(endStr, out var end)) return 0;
                    return unit.ToLower() switch
                    {
                        "day" => (int)(end - start).TotalDays,
                        "hour" => (int)(end - start).TotalHours,
                        _ => (int)(end - start).TotalDays
                    };
                });

            _logger.LogInformation("RuleEngineService initialized with in-memory SQLite");
        }

        public RuleResultEx ExecuteRule(string ruleExpression, Dictionary<string, object> parameters)
        {
            lock (_lock)
            {
                try
                {
                    var processedRule = ResolveTableReferences(ruleExpression);
                    var sql = SubstituteParameters(processedRule, parameters);

                    if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                        sql = $"SELECT {sql}";

                    _logger.LogDebug("Executing rule SQL: {Sql}", sql.Length > 120 ? sql[..120] : sql);

                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = sql;
                    var result = cmd.ExecuteScalar();

                    return result switch
                    {
                        long n => new RuleResultEx { Status = n != 0, ExecutedSql = sql },
                        double d => new RuleResultEx { Status = d != 0, ExecutedSql = sql },
                        string s => new RuleResultEx { Status = s != "0" && s != "false" && s != "", ExecutedSql = sql },
                        bool b => new RuleResultEx { Status = b, ExecutedSql = sql },
                        DBNull => new RuleResultEx { Status = false, ExecutedSql = sql },
                        null => new RuleResultEx { Status = false, ExecutedSql = sql },
                        _ => new RuleResultEx { Status = false, ExecutedSql = sql }
                    };
                }
                catch (Exception ex)
                {
                    return new RuleResultEx
                    {
                        Status = false,
                        HasErrored = true,
                        ErrorMessage = $"SQL Error: {ex.Message}",
                        ExecutedSql = ruleExpression
                    };
                }
            }
        }

        public RuleResultEx ExecuteRuleById(string ruleId, Dictionary<string, object> parameters)
        {
            if (!_rules.TryGetValue(ruleId, out var rule))
                return new RuleResultEx { Status = false, HasErrored = true, ErrorMessage = $"Rule '{ruleId}' not found" };

            if (string.IsNullOrEmpty(rule.Expression))
                return new RuleResultEx { Status = false, HasErrored = true, ErrorMessage = $"Rule '{ruleId}' has no expression" };

            return ExecuteRule(rule.Expression, parameters);
        }

        public List<Dictionary<string, object>> ExecuteQuery(string sql)
        {
            lock (_lock)
            {
                var rows = new List<Dictionary<string, object>>();
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.GetValue(i);
                    rows.Add(row);
                }
                return rows;
            }
        }

        public int LoadData(string datasetName, string tableName, JArray rows)
        {
            lock (_lock)
            {
                var fullName = $"{datasetName}_{tableName}";
                using var transaction = _connection.BeginTransaction();

                try
                {
                    if (_loadedTables.Contains(fullName))
                    {
                        ExecuteNonQuery($"DROP TABLE IF EXISTS [{fullName}]", transaction);
                        _loadedTables.Remove(fullName);
                    }

                    if (rows.Count == 0) return 0;

                    var firstRow = rows[0] as JObject;
                    if (firstRow == null) return 0;

                    var columns = firstRow.Properties().Select(p => p.Name).ToList();
                    var colDefs = string.Join(",", columns.Select(c => $"[{c}] TEXT"));
                    ExecuteNonQuery($"CREATE TABLE [{fullName}] ({colDefs})", transaction);

                    int loaded = 0;
                    foreach (var row in rows)
                    {
                        if (row is not JObject rowObj) continue;
                        var values = columns.Select(c =>
                        {
                            var val = rowObj[c];
                            if (val == null || val.Type == JTokenType.Null) return "NULL";
                            return $"'{val.ToString().Replace("'", "''")}'";
                        });
                        ExecuteNonQuery($"INSERT INTO [{fullName}] VALUES({string.Join(",", values)})", transaction);
                        loaded++;
                    }

                    _loadedTables.Add(fullName);
                    transaction.Commit();
                    _logger.LogInformation("Loaded {Rows} rows into [{Table}]", loaded, fullName);
                    return loaded;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public List<Dictionary<string, object>> GetTableSample(string tableName, int limit = 100)
        {
            lock (_lock)
            {
                var rows = new List<Dictionary<string, object>>();
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = $"SELECT * FROM [{tableName}] LIMIT {limit}";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                            row[reader.GetName(i)] = reader.GetValue(i);
                        rows.Add(row);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to sample table [{Table}]: {Error}", tableName, ex.Message);
                }
                return rows;
            }
        }

        public void EnsureDomainLoaded(int domainId)
        {
            if (_domains.ContainsKey(domainId))
                _logger.LogDebug("Domain {DomainId} already loaded", domainId);
        }

        public void RegisterRule(string ruleId, RuleDefinition rule)
        {
            lock (_lock)
            {
                _rules[ruleId] = rule;
                _logger.LogInformation("Registered rule: {RuleId}", ruleId);
            }
        }

        public void RegisterDomain(int domainId, DomainInfo info)
        {
            lock (_lock)
            {
                _domains[domainId] = info;
            }
        }

        public RuleDefinition? GetRule(string ruleId) =>
            _rules.TryGetValue(ruleId, out var r) ? r : null;

        public Dictionary<string, RuleDefinition> GetAllRules() => new(_rules);

        public List<string> GetLoadedTables()
        {
            lock (_lock) { return _loadedTables.ToList(); }
        }

        public object GetStatus()
        {
            lock (_lock)
            {
                return new
                {
                    tablesLoaded = _loadedTables.Count,
                    tables = _loadedTables.ToList(),
                    rulesRegistered = _rules.Count,
                    domainsRegistered = _domains.Count
                };
            }
        }

        private string ResolveTableReferences(string ruleText)
        {
            foreach (var fullTableName in _loadedTables)
            {
                var dotFormat = fullTableName.Replace("_", ".");
                ruleText = ruleText.Replace($"[{dotFormat}]", fullTableName);
                var parts = fullTableName.Split(new[] { '_' }, 2);
                if (parts.Length == 2)
                    ruleText = ruleText.Replace($"[{parts[1]}]", fullTableName);
            }
            return ruleText;
        }

        private string SubstituteParameters(string sql, Dictionary<string, object> parameters)
        {
            if (parameters == null) return sql;
            foreach (var pair in parameters)
            {
                var placeholder = "{" + pair.Key + "}";
                if (sql.Contains(placeholder))
                    sql = sql.Replace(placeholder, FormatSqlValue(pair.Value));
            }
            return sql;
        }

        private string FormatSqlValue(object? value)
        {
            if (value == null || value == DBNull.Value) return "NULL";
            return value switch
            {
                string s => $"'{s.Replace("'", "''")}'",
                DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
                bool b => b ? "1" : "0",
                decimal dec => dec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                double dbl => dbl.ToString(System.Globalization.CultureInfo.InvariantCulture),
                float flt => flt.ToString(System.Globalization.CultureInfo.InvariantCulture),
                int i => i.ToString(),
                long l => l.ToString(),
                IEnumerable list => FormatList(list),
                _ => $"'{value.ToString()?.Replace("'", "''")}'"
            };
        }

        private string FormatList(IEnumerable list)
        {
            var items = new List<string>();
            foreach (var item in list) items.Add(FormatSqlValue(item));
            return items.Count == 0 ? "(NULL)" : string.Join(", ", items);
        }

        private void ExecuteNonQuery(string sql, SqliteTransaction? transaction = null)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            if (transaction != null) cmd.Transaction = transaction;
            cmd.ExecuteNonQuery();
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
