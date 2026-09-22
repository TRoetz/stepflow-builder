using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SQLITE FORM DEFINITION STORE — same IFormDefinitionStore contract over the ui_forms
    // table, one row per (form_id, version). page_json TEXT holds the full UIPage JSON;
    // is_current_version marks the version flow steps resolve when they do not pin one.
    // Uses the shared StepFlowDataDb helper for connection + schema (+ legacy migration).
    // ═══════════════════════════════════════════════════════════════════════════════

    public class SqliteFormDefinitionStore : IFormDefinitionStore, IDisposable
    {
        private const string SelectColumns = "form_id, version, title, description, attribute_domain_name, schema_definition_name, page_json, is_current_version";

        private readonly string _dbPath;
        private readonly ILogger<SqliteFormDefinitionStore>? _logger;
        private readonly object _lock = new();

        public SqliteFormDefinitionStore(string dbPath, ILogger<SqliteFormDefinitionStore>? logger = null)
        {
            _dbPath = Path.GetFullPath(dbPath);
            _logger = logger;
            using var conn = StepFlowDataDb.Open(_dbPath);
            StepFlowDataDb.EnsureSchema(conn);
        }

        public string ProviderName => "sqlite";

        private static void ValidateId(string formId)
        {
            if (string.IsNullOrWhiteSpace(formId)) throw new ArgumentException("FormDefinition requires a non-empty FormId");
        }

        private static void ValidateVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("FormDefinition requires a non-empty Version");
        }

        public IReadOnlyList<FormDefinition> GetAll()
        {
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var cmd = conn.CreateCommand();
                // Numeric-aware version ordering (versions are integer strings; non-numeric labels sort last).
                cmd.CommandText = $"""
                    SELECT {SelectColumns} FROM ui_forms
                    ORDER BY form_id, CAST(CASE WHEN version GLOB '[0-9]*' THEN version ELSE '2147483647' END AS INTEGER), version;
                    """;
                using var reader = cmd.ExecuteReader();
                var forms = new List<FormDefinition>();
                while (reader.Read())
                    forms.Add(ReadRow(reader));
                return forms;
            }
        }

        public FormDefinition? Get(string formId)
        {
            ValidateId(formId);
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT {SelectColumns} FROM ui_forms WHERE form_id = $id AND is_current_version = 1;
                    """;
                cmd.Parameters.AddWithValue("$id", formId);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadRow(reader) : null;
            }
        }

        public FormDefinition? Get(string formId, string version)
        {
            ValidateId(formId);
            ValidateVersion(version);
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT {SelectColumns} FROM ui_forms WHERE form_id = $id AND version = $version;
                    """;
                cmd.Parameters.AddWithValue("$id", formId);
                cmd.Parameters.AddWithValue("$version", version);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadRow(reader) : null;
            }
        }

        public void Save(FormDefinition def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.FormId))
                throw new ArgumentException("FormDefinition requires a non-empty FormId");
            ValidateVersion(def.Version);
            if (!(def.Page is JObject page) || !(page["RootElements"] is JArray))
                throw new ArgumentException($"Form '{def.FormId}' Page must be an object with a RootElements array");

            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var tx = conn.BeginTransaction();
                try
                {
                    if (def.IsCurrentVersion)
                    {
                        // Clear the flag on every other version of this form first so at most one row is current.
                        using var clear = conn.CreateCommand();
                        clear.Transaction = tx;
                        clear.CommandText = "UPDATE ui_forms SET is_current_version = 0 WHERE form_id = $id AND version <> $version;";
                        clear.Parameters.AddWithValue("$id", def.FormId);
                        clear.Parameters.AddWithValue("$version", def.Version);
                        clear.ExecuteNonQuery();
                    }

                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO ui_forms (form_id, version, title, description, attribute_domain_name, schema_definition_name, page_json, is_current_version, updated_at_utc)
                        VALUES ($id, $version, $title, $desc, $domain, $schema, $page, $current, $updated)
                        ON CONFLICT(form_id, version) DO UPDATE SET
                          title = excluded.title,
                          description = excluded.description,
                          attribute_domain_name = excluded.attribute_domain_name,
                          schema_definition_name = excluded.schema_definition_name,
                          page_json = excluded.page_json,
                          is_current_version = excluded.is_current_version,
                          updated_at_utc = excluded.updated_at_utc;
                        """;
                    cmd.Parameters.AddWithValue("$id", def.FormId);
                    cmd.Parameters.AddWithValue("$version", def.Version);
                    cmd.Parameters.AddWithValue("$title", (object?)def.Title ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$desc", (object?)def.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$domain", string.IsNullOrWhiteSpace(def.AttributeDomainName) ? (object)DBNull.Value : def.AttributeDomainName!);
                    cmd.Parameters.AddWithValue("$schema", (object?)def.SchemaDefinitionName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$page", def.Page.ToString(Newtonsoft.Json.Formatting.None));
                    cmd.Parameters.AddWithValue("$current", def.IsCurrentVersion ? 1 : 0);
                    cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
                    cmd.ExecuteNonQuery();

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        public bool Delete(string formId)
        {
            ValidateId(formId);
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM ui_forms WHERE form_id = $id;";
                cmd.Parameters.AddWithValue("$id", formId);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        private static FormDefinition ReadRow(SqliteDataReader reader) => new()
        {
            FormId = reader.GetString(0),
            Version = reader.GetString(1),
            Title = reader.IsDBNull(2) ? "" : reader.GetString(2),
            Description = reader.IsDBNull(3) ? null : reader.GetString(3),
            AttributeDomainName = reader.IsDBNull(4) ? null : reader.GetString(4),
            SchemaDefinitionName = reader.IsDBNull(5) ? null : reader.GetString(5),
            Page = JToken.Parse(reader.GetString(6)),
            IsCurrentVersion = reader.GetInt64(7) != 0
        };

        public void Dispose() { }
    }
}
