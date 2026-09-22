using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SQLITE ATTRIBUTE DOMAIN STORE — same IAttributeDomainStore contract over the
    // attribute_domains / entity_attributes tables (domain_name is the key; the optional
    // schema link lives in plain schema_definition_name + schema_definition_version columns).
    // Save replaces the attribute set wholesale (delete + re-insert with sort_order) and
    // sets/clears the schema link. Name lookups are case-insensitive (COLLATE NOCASE) to
    // match the JSON provider's behavior. Readers run sequentially on one connection
    // (no MARS in Microsoft.Data.Sqlite).
    // ═══════════════════════════════════════════════════════════════════════════════

    public class SqliteAttributeDomainStore : IAttributeDomainStore, IDisposable
    {
        private readonly string _dbPath;
        private readonly ILogger<SqliteAttributeDomainStore>? _logger;
        private readonly object _lock = new();

        public SqliteAttributeDomainStore(string dbPath, ILogger<SqliteAttributeDomainStore>? logger = null)
        {
            _dbPath = Path.GetFullPath(dbPath);
            _logger = logger;
            using var conn = StepFlowDataDb.Open(_dbPath);
            StepFlowDataDb.EnsureSchema(conn);
        }

        public string ProviderName => "sqlite";

        private static EntityAttribute ReadAttribute(SqliteDataReader r) => new()
        {
            AttributeName = r.GetString(0),
            DataType = (AttributeDataType)r.GetInt32(1),
            Description = r.IsDBNull(2) ? "" : r.GetString(2),
            DisplayName = r.IsDBNull(3) ? "" : r.GetString(3),
            Placeholder = r.IsDBNull(4) ? "" : r.GetString(4),
            HelpText = r.IsDBNull(5) ? "" : r.GetString(5),
            Visible = r.GetInt64(6) == 1,
            ReadOnly = r.GetInt64(7) == 1,
            PrimaryKey = r.GetInt64(8) == 1,
            ValidationSchemaJson = r.IsDBNull(9) ? "" : r.GetString(9)
        };

        private static List<EntityAttribute> LoadAttributes(SqliteConnection conn, string domainName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT attribute_name, data_type, description, display_name, placeholder, help_text, visible, read_only, primary_key, validation_schema_json
                FROM entity_attributes WHERE domain_name = $name COLLATE NOCASE ORDER BY sort_order;
                """;
            cmd.Parameters.AddWithValue("$name", domainName);
            using var reader = cmd.ExecuteReader();
            var attributes = new List<EntityAttribute>();
            while (reader.Read())
                attributes.Add(ReadAttribute(reader));
            return attributes;
        }

        public IReadOnlyList<AttributeDomain> GetAll()
        {
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                var headers = new List<(string name, string version, string description, bool current, string? schemaName, string? schemaVersion)>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT domain_name, version, description, is_current_version, schema_definition_name, schema_definition_version FROM attribute_domains ORDER BY domain_name;";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        headers.Add((reader.GetString(0),
                            reader.IsDBNull(1) ? "1" : reader.GetString(1),
                            reader.IsDBNull(2) ? "" : reader.GetString(2),
                            reader.GetInt64(3) == 1,
                            reader.IsDBNull(4) ? null : reader.GetString(4),
                            reader.IsDBNull(5) ? null : reader.GetString(5)));
                }

                var domains = new List<AttributeDomain>();
                foreach (var h in headers)
                {
                    var d = new AttributeDomain
                    {
                        Version = h.version,
                        AttributeDomainName = h.name,
                        Description = h.description,
                        IsCurrentVersion = h.current,
                        Attributes = LoadAttributes(conn, h.name)
                    };
                    if (h.schemaName != null)
                        d.SchemaDefinition = new SchemaDefinition
                        {
                            SchemaDefinitionName = h.schemaName,
                            Version = h.schemaVersion ?? "1",
                            AttributeDomain = true
                        };
                    domains.Add(d);
                }
                return domains;
            }
        }

        public (AttributeDomain? domain, SchemaDefinition? schema) GetByName(string domainName)
        {
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                AttributeDomain? domain = null;
                string? linkedSchemaName = null;
                string? linkedSchemaVersion = null;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT domain_name, version, description, is_current_version, schema_definition_name, schema_definition_version
                        FROM attribute_domains WHERE domain_name = $name COLLATE NOCASE;
                        """;
                    cmd.Parameters.AddWithValue("$name", domainName);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        linkedSchemaName = reader.IsDBNull(4) ? null : reader.GetString(4);
                        linkedSchemaVersion = reader.IsDBNull(5) ? null : reader.GetString(5);
                        domain = new AttributeDomain
                        {
                            Version = reader.IsDBNull(1) ? "1" : reader.GetString(1),
                            AttributeDomainName = reader.GetString(0),
                            Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            IsCurrentVersion = reader.GetInt64(3) == 1,
                            Attributes = LoadAttributes(conn, domainName)
                        };
                    }
                }

                if (domain == null || linkedSchemaName == null) return (domain, null);

                // The link is a plain reference: always report the pinned name + version, enriching from the
                // registry row when present (a dangling link still surfaces its coordinates).
                var schemaDef = new SchemaDefinition
                {
                    SchemaDefinitionName = linkedSchemaName,
                    Version = linkedSchemaVersion ?? "1",
                    Description = "",
                    AttributeDomain = true
                };
                using (var sCmd = conn.CreateCommand())
                {
                    // Legacy rows without a pinned version resolve to the latest registry entry.
                    sCmd.CommandText = linkedSchemaVersion == null
                        ? """
                            SELECT version, description FROM schema_definitions
                            WHERE schema_definition_name = $name COLLATE NOCASE
                            ORDER BY CAST(CASE WHEN version GLOB '[0-9]*' THEN version ELSE '2147483647' END AS INTEGER) DESC LIMIT 1;
                            """
                        : "SELECT version, description FROM schema_definitions WHERE schema_definition_name = $name COLLATE NOCASE AND version = $version;";
                    sCmd.Parameters.AddWithValue("$name", linkedSchemaName);
                    if (linkedSchemaVersion != null) sCmd.Parameters.AddWithValue("$version", linkedSchemaVersion);
                    using var sReader = sCmd.ExecuteReader();
                    if (sReader.Read())
                    {
                        schemaDef.Version = sReader.IsDBNull(0) ? "1" : sReader.GetString(0);
                        schemaDef.Description = sReader.IsDBNull(1) ? "" : sReader.GetString(1);
                    }
                }

                return (domain, schemaDef);
            }
        }

        public void Save(AttributeDomain domain, SchemaDefinition? schema = null)
        {
            if (domain == null || string.IsNullOrWhiteSpace(domain.AttributeDomainName))
                throw new ArgumentException("AttributeDomain requires a non-empty AttributeDomainName");

            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var tx = conn.BeginTransaction();

                using var dCmd = conn.CreateCommand();
                dCmd.Transaction = tx;
                dCmd.CommandText = """
                    INSERT INTO attribute_domains (domain_name, version, description, is_current_version, schema_definition_name, schema_definition_version)
                    VALUES ($name, $version, $desc, $current, $schema, $schemaVersion)
                    ON CONFLICT(domain_name) DO UPDATE SET
                      version = excluded.version,
                      description = excluded.description,
                      is_current_version = excluded.is_current_version,
                      schema_definition_name = excluded.schema_definition_name,
                      schema_definition_version = excluded.schema_definition_version;
                    """;
                dCmd.Parameters.AddWithValue("$name", domain.AttributeDomainName);
                dCmd.Parameters.AddWithValue("$version", (object?)domain.Version ?? "1");
                dCmd.Parameters.AddWithValue("$desc", (object?)domain.Description ?? DBNull.Value);
                dCmd.Parameters.AddWithValue("$current", domain.IsCurrentVersion ? 1L : 0L);
                var hasSchemaLink = schema != null && !string.IsNullOrEmpty(schema.SchemaDefinitionName);
                dCmd.Parameters.AddWithValue("$schema", hasSchemaLink ? (object)schema!.SchemaDefinitionName! : DBNull.Value);
                dCmd.Parameters.AddWithValue("$schemaVersion", hasSchemaLink ? (object)(schema!.Version ?? "1") : DBNull.Value);
                dCmd.ExecuteNonQuery();

                using var delCmd = conn.CreateCommand();
                delCmd.Transaction = tx;
                delCmd.CommandText = "DELETE FROM entity_attributes WHERE domain_name = $name COLLATE NOCASE;";
                delCmd.Parameters.AddWithValue("$name", domain.AttributeDomainName);
                delCmd.ExecuteNonQuery();

                if (domain.Attributes != null)
                {
                    var attrs = domain.Attributes.ToList();
                    for (var i = 0; i < attrs.Count; i++)
                    {
                        // Fresh command per row: Microsoft.Data.Sqlite does not reliably reuse a
                        // parameterized SqliteCommand across multiple executions.
                        using var aCmd = conn.CreateCommand();
                        aCmd.Transaction = tx;
                        aCmd.CommandText = """
                            INSERT INTO entity_attributes (domain_name, attribute_name, data_type, description, display_name, placeholder, help_text, visible, read_only, primary_key, validation_schema_json, sort_order)
                            VALUES ($name, $attr, $type, $desc, $display, $placeholder, $help, $visible, $readonly, $pk, $validation, $order);
                            """;
                        var a = attrs[i];
                        aCmd.Parameters.AddWithValue("$name", domain.AttributeDomainName);
                        aCmd.Parameters.AddWithValue("$attr", a.AttributeName ?? "");
                        aCmd.Parameters.AddWithValue("$type", (int)a.DataType);
                        aCmd.Parameters.AddWithValue("$desc", (object?)a.Description ?? DBNull.Value);
                        aCmd.Parameters.AddWithValue("$display", (object?)a.DisplayName ?? DBNull.Value);
                        aCmd.Parameters.AddWithValue("$placeholder", (object?)a.Placeholder ?? DBNull.Value);
                        aCmd.Parameters.AddWithValue("$help", (object?)a.HelpText ?? DBNull.Value);
                        aCmd.Parameters.AddWithValue("$visible", a.Visible ? 1L : 0L);
                        aCmd.Parameters.AddWithValue("$readonly", a.ReadOnly ? 1L : 0L);
                        aCmd.Parameters.AddWithValue("$pk", a.PrimaryKey ? 1L : 0L);
                        aCmd.Parameters.AddWithValue("$validation", string.IsNullOrEmpty(a.ValidationSchemaJson) ? (object)DBNull.Value : a.ValidationSchemaJson!);
                        aCmd.Parameters.AddWithValue("$order", i);
                        aCmd.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }
        }

        public bool Delete(string domainName)
        {
            lock (_lock)
            {
                using var conn = StepFlowDataDb.Open(_dbPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM attribute_domains WHERE domain_name = $name COLLATE NOCASE;";
                cmd.Parameters.AddWithValue("$name", domainName);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public void Dispose() { }
    }
}
