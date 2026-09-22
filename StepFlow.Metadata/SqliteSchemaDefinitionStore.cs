using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SQLITE SCHEMA DEFINITION STORE — same ISchemaDefinitionStore contract over the
    // schema_definitions table, one row per (schema_definition_name, version). The JSON
    // body lives in definition TEXT. Uses the shared StepFlowDataDb helper for
    // connection + schema (+ legacy migration from the single-version layout).
    // ═══════════════════════════════════════════════════════════════════════════════

    public class SqliteSchemaDefinitionStore : ISchemaDefinitionStore, IDisposable
    {
        private const string SelectColumns = "schema_definition_name, version, description, definition, definition_from, is_attribute_domain, created_by, created_on";

        // Numeric-aware latest-version ordering (versions are integer strings; non-numeric labels sort first).
        private const string VersionOrderDesc = "CAST(CASE WHEN version GLOB '[0-9]*' THEN version ELSE '2147483647' END AS INTEGER) DESC, version DESC";

        private readonly string _dbPath;
        private readonly ILogger<SqliteSchemaDefinitionStore>? _logger;
        private readonly object _lock = new();

        public SqliteSchemaDefinitionStore(string dbPath, ILogger<SqliteSchemaDefinitionStore>? logger = null)
        {
            _dbPath = Path.GetFullPath(dbPath);
            _logger = logger;
            using var conn = StepFlowDataDb.Open(_dbPath);
            StepFlowDataDb.EnsureSchema(conn);
        }

        public string ProviderName => "sqlite";

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("SchemaDefinition requires a non-empty SchemaDefinitionName");
        }

        private static void ValidateVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("SchemaDefinition requires a non-empty Version");
        }

        public IReadOnlyList<SchemaDefinition> GetAll()
        {
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT {SelectColumns} FROM schema_definitions
                    ORDER BY schema_definition_name, CAST(CASE WHEN version GLOB '[0-9]*' THEN version ELSE '2147483647' END AS INTEGER), version;
                    """;
                using var reader = cmd.ExecuteReader();
                var schemas = new List<SchemaDefinition>();
                while (reader.Read())
                    schemas.Add(ReadRow(reader));
                return schemas;
            }
        }

        public SchemaDefinition? Get(string name)
        {
            ValidateName(name);
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT {SelectColumns} FROM schema_definitions WHERE schema_definition_name = $name COLLATE NOCASE ORDER BY {VersionOrderDesc} LIMIT 1;
                    """;
                cmd.Parameters.AddWithValue("$name", name);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadRow(reader) : null;
            }
        }

        public SchemaDefinition? Get(string name, string version)
        {
            ValidateName(name);
            ValidateVersion(version);
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT {SelectColumns} FROM schema_definitions WHERE schema_definition_name = $name COLLATE NOCASE AND version = $version;
                    """;
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$version", version);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadRow(reader) : null;
            }
        }

        public void Save(SchemaDefinition def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.SchemaDefinitionName))
                throw new ArgumentException("SchemaDefinition requires a non-empty SchemaDefinitionName");
            ValidateVersion(def.Version);
            if (string.IsNullOrWhiteSpace(def.Definition))
                throw new ArgumentException($"Schema '{def.SchemaDefinitionName}' requires a non-empty Definition");
            try
            {
                JToken.Parse(def.Definition);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Schema '{def.SchemaDefinitionName}' Definition must be valid JSON", ex);
            }

            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO schema_definitions (schema_definition_name, version, description, definition, definition_from, is_attribute_domain, created_by, created_on)
                    VALUES ($name, $version, $desc, $definition, $from, $domain, $createdBy, $createdOn)
                    ON CONFLICT(schema_definition_name, version) DO UPDATE SET
                      description = excluded.description,
                      definition = excluded.definition,
                      definition_from = excluded.definition_from,
                      is_attribute_domain = excluded.is_attribute_domain,
                      created_by = excluded.created_by,
                      created_on = excluded.created_on;
                    """;
                cmd.Parameters.AddWithValue("$name", def.SchemaDefinitionName);
                cmd.Parameters.AddWithValue("$version", def.Version);
                cmd.Parameters.AddWithValue("$desc", (object?)def.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$definition", def.Definition);
                cmd.Parameters.AddWithValue("$from", (object?)def.DefinitionFrom ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$domain", def.AttributeDomain ? 1 : 0);
                cmd.Parameters.AddWithValue("$createdBy", (object?)def.CreatedBy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$createdOn", def.CreatedOn == default ? (object)DBNull.Value : def.CreatedOn.ToString("O"));
                cmd.ExecuteNonQuery();
            }
        }

        public bool Delete(string name, string version)
        {
            ValidateName(name);
            ValidateVersion(version);
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM schema_definitions WHERE schema_definition_name = $name COLLATE NOCASE AND version = $version;";
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$version", version);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        private static SchemaDefinition ReadRow(SqliteDataReader reader) => new()
        {
            SchemaDefinitionName = reader.GetString(0),
            Version = reader.GetString(1),
            Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
            Definition = reader.IsDBNull(3) ? "" : reader.GetString(3),
            DefinitionFrom = reader.IsDBNull(4) ? "" : reader.GetString(4),
            AttributeDomain = reader.GetInt64(5) != 0,
            CreatedBy = reader.IsDBNull(6) ? "" : reader.GetString(6),
            CreatedOn = reader.IsDBNull(7) || !DateTime.TryParse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind, out var createdOn)
                ? default
                : createdOn
        };

        public void Dispose() { }
    }
}
