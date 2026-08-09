using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // DUCKDB TRANSFORM SERVICE
    // In-process SQL engine for data transformations — replaces SSIS Data Flow
    // components. Supports SQL on Parquet, CSV, JSON, and in-memory tables.
    //
    // Resource scheme: transform://<operation>
    //   - transform://query    — execute arbitrary SQL query
    //   - transform://csv      — load CSV and query
    //   - transform://json     — load JSON array and query
    //   - transform://parquet  — load Parquet and query
    // ═══════════════════════════════════════════════════════════════════════════════

    public class DuckDbTransformService : IDisposable
    {
        private readonly DuckDBConnection _connection;
        private readonly ILogger<DuckDbTransformService> _logger;
        private readonly Dictionary<string, string> _namedTables = new();

        public DuckDbTransformService(ILogger<DuckDbTransformService> logger)
        {
            _logger = logger;
            _connection = new DuckDBConnection("DataSource=:memory:");
            _connection.Open();
            _logger.LogInformation("DuckDbTransformService initialized with in-memory database");
        }

        // ── Execute SQL Transform ───────────────────────────────────────────────

        /// <summary>
        /// Execute a SQL query and return results as a list of JSON objects.
        /// </summary>
        public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object>? parameters = null)
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        cmd.Parameters.Add(new DuckDBParameter($"${param.Key}", param.Value ?? DBNull.Value));
                    }
                }

                return ReadResults(cmd);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DuckDB query failed: {Sql}", sql.Length > 200 ? sql[..200] : sql);
                throw;
            }
        }

        /// <summary>
        /// Execute a SQL statement (INSERT, UPDATE, CREATE, etc.) and return affected rows.
        /// </summary>
        public int ExecuteNonQuery(string sql, Dictionary<string, object>? parameters = null)
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        cmd.Parameters.Add(new DuckDBParameter($"${param.Key}", param.Value ?? DBNull.Value));
                    }
                }

                return cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DuckDB non-query failed: {Sql}", sql.Length > 200 ? sql[..200] : sql);
                throw;
            }
        }

        // ── Load Data ───────────────────────────────────────────────────────────

        /// <summary>
        /// Load a JSON array into a named table for subsequent queries.
        /// </summary>
        public int LoadJsonData(string tableName, JArray rows)
        {
            if (rows.Count == 0) return 0;

            // Write JSON to a temp file, then load via read_json_auto
            var tempPath = Path.GetTempFileName() + ".json";
            try
            {
                File.WriteAllText(tempPath, rows.ToString(Formatting.None));
                var sql = $"CREATE OR REPLACE TABLE \"{tableName}\" AS SELECT * FROM read_json_auto('{EscapeSingleQuotes(tempPath)}')";
                ExecuteNonQuery(sql);
                _namedTables[tableName] = "json";
                _logger.LogInformation("Loaded {Rows} rows into [{Table}] from JSON", rows.Count, tableName);
                return rows.Count;
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        }

        /// <summary>
        /// Load a CSV file into a named table.
        /// </summary>
        public int LoadCsvFile(string tableName, string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"CSV file not found: {filePath}", filePath);

            var sql = $"CREATE OR REPLACE TABLE \"{tableName}\" AS SELECT * FROM read_csv_auto('{EscapeSingleQuotes(filePath)}')";
            ExecuteNonQuery(sql);
            _namedTables[tableName] = "csv";
            var count = ExecuteQuery($"SELECT COUNT(*) as cnt FROM \"{tableName}\"");
            _logger.LogInformation("Loaded CSV into [{Table}] from {Path}", tableName, filePath);
            return count.Count > 0 ? Convert.ToInt32(count[0]["cnt"]) : 0;
        }

        /// <summary>
        /// Load a Parquet file into a named table.
        /// </summary>
        public int LoadParquetFile(string tableName, string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Parquet file not found: {filePath}", filePath);

            var sql = $"CREATE OR REPLACE TABLE \"{tableName}\" AS SELECT * FROM read_parquet('{EscapeSingleQuotes(filePath)}')";
            ExecuteNonQuery(sql);
            _namedTables[tableName] = "parquet";
            var count = ExecuteQuery($"SELECT COUNT(*) as cnt FROM \"{tableName}\"");
            _logger.LogInformation("Loaded Parquet into [{Table}] from {Path}", tableName, filePath);
            return count.Count > 0 ? Convert.ToInt32(count[0]["cnt"]) : 0;
        }

        /// <summary>
        /// Load CSV data from a string.
        /// </summary>
        public int LoadCsvString(string tableName, string csvContent)
        {
            // Write to temp file and load
            var tempPath = Path.GetTempFileName() + ".csv";
            try
            {
                File.WriteAllText(tempPath, csvContent);
                return LoadCsvFile(tableName, tempPath);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        }

        // ── Query Helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Get a sample of rows from a table.
        /// </summary>
        public List<Dictionary<string, object>> GetTableSample(string tableName, int limit = 100)
        {
            try
            {
                return ExecuteQuery($"SELECT * FROM \"{tableName}\" LIMIT {limit}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to sample table [{Table}]: {Error}", tableName, ex.Message);
                return new();
            }
        }

        /// <summary>
        /// Get schema info for a table.
        /// </summary>
        public List<Dictionary<string, object>> GetTableSchema(string tableName)
        {
            try
            {
                return ExecuteQuery($"DESCRIBE \"{tableName}\"");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to get schema for [{Table}]: {Error}", tableName, ex.Message);
                return new();
            }
        }

        /// <summary>
        /// List all table names.
        /// </summary>
        public List<string> ListTables()
        {
            var result = ExecuteQuery("SHOW TABLES");
            return result.Select(r => r.Values.FirstOrDefault()?.ToString() ?? "").ToList();
        }

        // ── Transform Operations (SSIS Data Flow Equivalents) ───────────────────

        /// <summary>
        /// Filter rows — equivalent to SSIS Filter/Conditional Split.
        /// </summary>
        public List<Dictionary<string, object>> Filter(string tableName, string whereClause)
        {
            return ExecuteQuery($"SELECT * FROM \"{tableName}\" WHERE {whereClause}");
        }

        /// <summary>
        /// Select/Project columns — equivalent to SSIS Derived Column.
        /// </summary>
        public List<Dictionary<string, object>> Project(string tableName, string columnExpression)
        {
            return ExecuteQuery($"SELECT {columnExpression} FROM \"{tableName}\"");
        }

        /// <summary>
        /// Aggregate rows — equivalent to SSIS Aggregate transformation.
        /// </summary>
        public List<Dictionary<string, object>> Aggregate(string tableName, string groupBy, string aggregations)
        {
            return ExecuteQuery($"SELECT {groupBy}, {aggregations} FROM \"{tableName}\" GROUP BY {groupBy}");
        }

        /// <summary>
        /// Sort rows — equivalent to SSIS Sort transformation.
        /// </summary>
        public List<Dictionary<string, object>> Sort(string tableName, string orderBy)
        {
            return ExecuteQuery($"SELECT * FROM \"{tableName}\" ORDER BY {orderBy}");
        }

        /// <summary>
        /// Deduplicate rows — equivalent to SSIS Remove Duplicates.
        /// </summary>
        public List<Dictionary<string, object>> Deduplicate(string tableName, string keyColumns)
        {
            return ExecuteQuery($"SELECT DISTINCT {keyColumns} FROM \"{tableName}\"");
        }

        /// <summary>
        /// Lookup/join — equivalent to SSIS Lookup transformation.
        /// </summary>
        public List<Dictionary<string, object>> Lookup(string sourceTable, string lookupTable, string joinCondition, string selectColumns = "*")
        {
            return ExecuteQuery(
                $"SELECT {selectColumns} FROM \"{sourceTable}\" s " +
                $"INNER JOIN \"{lookupTable}\" l ON {joinCondition}"
            );
        }

        // ── Transform Execution (used by ResourceInvoker) ────────────────────────

        /// <summary>
        /// Execute a transform operation from a JObject input.
        /// Input format:
        /// {
        ///   "operation": "query" | "filter" | "project" | "aggregate" | ...
        ///   "sql": "SELECT ..."          (for query operation)
        ///   "table": "tableName"         (for table operations)
        ///   "parameters": { ... }        (optional SQL parameters)
        ///   "data": [ ... ]             (optional inline data to load first)
        /// }
        /// </summary>
        public JObject ExecuteTransform(JObject input)
        {
            var operation = input["operation"]?.ToString() ?? "query";
            var parameters = input["parameters"]?.ToObject<Dictionary<string, object>>() ?? new();
            var result = new JObject();

            try
            {
                // If inline data provided, load it first
                if (input["data"] is JArray dataArray && input["table"] != null)
                {
                    LoadJsonData(input["table"]!.ToString(), dataArray);
                }

                switch (operation)
                {
                    case "query":
                        {
                            var sql = input["sql"]?.ToString();
                            if (string.IsNullOrEmpty(sql))
                                throw new ArgumentException("Operation 'query' requires 'sql' field");
                            var rows = ExecuteQuery(sql, parameters);
                            result["rows"] = JToken.FromObject(rows);
                            result["rowCount"] = rows.Count;
                            break;
                        }

                    case "filter":
                        {
                            var table = input["table"]?.ToString() ?? throw new ArgumentException("Operation 'filter' requires 'table'");
                            var where = input["where"]?.ToString() ?? throw new ArgumentException("Operation 'filter' requires 'where'");
                            var rows = Filter(table, where);
                            result["rows"] = JToken.FromObject(rows);
                            result["rowCount"] = rows.Count;
                            break;
                        }

                    case "project":
                        {
                            var table = input["table"]?.ToString() ?? throw new ArgumentException("Operation 'project' requires 'table'");
                            var columns = input["columns"]?.ToString() ?? "*";
                            var rows = Project(table, columns);
                            result["rows"] = JToken.FromObject(rows);
                            result["rowCount"] = rows.Count;
                            break;
                        }

                    case "aggregate":
                        {
                            var table = input["table"]?.ToString() ?? throw new ArgumentException("Operation 'aggregate' requires 'table'");
                            var groupBy = input["groupBy"]?.ToString() ?? throw new ArgumentException("Operation 'aggregate' requires 'groupBy'");
                            var aggregations = input["aggregations"]?.ToString() ?? "COUNT(*)";
                            var rows = Aggregate(table, groupBy, aggregations);
                            result["rows"] = JToken.FromObject(rows);
                            result["rowCount"] = rows.Count;
                            break;
                        }

                    case "sort":
                        {
                            var table = input["table"]?.ToString() ?? throw new ArgumentException("Operation 'sort' requires 'table'");
                            var orderBy = input["orderBy"]?.ToString() ?? throw new ArgumentException("Operation 'sort' requires 'orderBy'");
                            var rows = Sort(table, orderBy);
                            result["rows"] = JToken.FromObject(rows);
                            result["rowCount"] = rows.Count;
                            break;
                        }

                    case "lookup":
                        {
                            var source = input["sourceTable"]?.ToString() ?? throw new ArgumentException("Operation 'lookup' requires 'sourceTable'");
                            var lookup = input["lookupTable"]?.ToString() ?? throw new ArgumentException("Operation 'lookup' requires 'lookupTable'");
                            var join = input["joinCondition"]?.ToString() ?? throw new ArgumentException("Operation 'lookup' requires 'joinCondition'");
                            var select = input["selectColumns"]?.ToString() ?? "*";
                            var rows = Lookup(source, lookup, join, select);
                            result["rows"] = JToken.FromObject(rows);
                            result["rowCount"] = rows.Count;
                            break;
                        }

                    case "schema":
                        {
                            var table = input["table"]?.ToString() ?? throw new ArgumentException("Operation 'schema' requires 'table'");
                            var schema = GetTableSchema(table);
                            result["schema"] = JToken.FromObject(schema);
                            break;
                        }

                    case "sample":
                        {
                            var table = input["table"]?.ToString() ?? throw new ArgumentException("Operation 'sample' requires 'table'");
                            var limit = (int)(input["limit"]?.Value<int>() ?? 100);
                            var rows = GetTableSample(table, limit);
                            result["rows"] = JToken.FromObject(rows);
                            result["rowCount"] = rows.Count;
                            break;
                        }

                    case "execute":
                        {
                            var sql = input["sql"]?.ToString();
                            if (string.IsNullOrEmpty(sql))
                                throw new ArgumentException("Operation 'execute' requires 'sql' field");
                            ExecuteNonQuery(sql);
                            result["affectedRows"] = 1;
                            break;
                        }

                    default:
                        throw new ArgumentException($"Unknown transform operation: {operation}");
                }

                result["success"] = true;
                result["operation"] = operation;
            }
            catch (Exception ex)
            {
                result["success"] = false;
                result["error"] = ex.Message;
                result["operation"] = operation;
            }

            return result;
        }

        // ── Status ──────────────────────────────────────────────────────────────

        public object GetStatus()
        {
            return new
            {
                tablesLoaded = _namedTables.Count,
                tables = _namedTables.Select(kv => new { name = kv.Key, source = kv.Value }).ToList(),
                allTables = ListTables()
            };
        }

        // ── Internal Helpers ────────────────────────────────────────────────────

        private List<Dictionary<string, object>> ReadResults(DuckDBCommand cmd)
        {
            var rows = new List<Dictionary<string, object>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    row[reader.GetName(i)] = value == DBNull.Value ? null : value;
                }
                rows.Add(row);
            }
            return rows;
        }

        private string EscapeSingleQuotes(string input) => input.Replace("'", "''");

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
