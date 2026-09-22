using Microsoft.Data.Sqlite;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // STEPFLOW DATA DB — shared SQLite helper for the provider-backed form subsystem.
    // Owns the connection factory, idempotent schema DDL, and the one-time migration
    // from the legacy single-version layout (ui_forms PK form_id; schema_definitions
    // PK name only; attribute_domains with an FK to schema_definitions) to the
    // versioned layout: ui_forms PK (form_id, version), schema_definitions PK
    // (schema_definition_name, version), and a plain schema_definition_version column
    // on attribute_domains (no FK — reference validation lives in the controllers).
    // Both Sqlite* stores call EnsureSchema on init so either store can create/verify
    // the database independently.
    // ═══════════════════════════════════════════════════════════════════════════════

    public static class StepFlowDataDb
    {
        private const string SchemaDdl = """
            CREATE TABLE IF NOT EXISTS schema_definitions (
              schema_definition_name TEXT NOT NULL, version TEXT NOT NULL, description TEXT,
              definition TEXT, definition_from TEXT, is_attribute_domain INTEGER NOT NULL DEFAULT 0,
              created_by TEXT, created_on TEXT, PRIMARY KEY(schema_definition_name, version));
            CREATE TABLE IF NOT EXISTS attribute_domains (
              domain_name TEXT PRIMARY KEY, version TEXT, description TEXT,
              schema_definition_name TEXT, schema_definition_version TEXT,
              is_current_version INTEGER NOT NULL DEFAULT 1, supersedes_domain_id INTEGER,
              created_at_utc TEXT);
            CREATE TABLE IF NOT EXISTS entity_attributes (
              attribute_id INTEGER PRIMARY KEY AUTOINCREMENT,
              domain_name TEXT NOT NULL REFERENCES attribute_domains(domain_name) ON DELETE CASCADE,
              attribute_name TEXT NOT NULL, data_type INTEGER NOT NULL DEFAULT 0,
              description TEXT, display_name TEXT, placeholder TEXT, help_text TEXT,
              visible INTEGER NOT NULL DEFAULT 1, read_only INTEGER NOT NULL DEFAULT 0,
              primary_key INTEGER NOT NULL DEFAULT 0, validation_schema_json TEXT,
              sort_order INTEGER NOT NULL DEFAULT 0, UNIQUE (domain_name, attribute_name));
            CREATE TABLE IF NOT EXISTS ui_forms (
              form_id TEXT NOT NULL, version TEXT NOT NULL, title TEXT, description TEXT,
              attribute_domain_name TEXT, schema_definition_name TEXT, page_json TEXT NOT NULL,
              is_current_version INTEGER NOT NULL DEFAULT 1, updated_at_utc TEXT,
              PRIMARY KEY(form_id, version));
            """;

        /// <summary>Opens a connection to the database at <paramref name="dbPath"/> (file created on first use).</summary>
        public static SqliteConnection Open(string dbPath)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Mode=ReadWriteCreate: create the file when missing; foreign keys enforced per connection.
            var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate");
            conn.Open();
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = ON;";
                pragma.ExecuteNonQuery();
            }
            return conn;
        }

        /// <summary>Runs the legacy→versioned migration when needed, then the full DDL idempotently.</summary>
        public static void EnsureSchema(SqliteConnection conn)
        {
            MigrateLegacySchema(conn);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = SchemaDdl;
            cmd.ExecuteNonQuery();
        }

        // ── Legacy migration (idempotent; no-op on fresh or already-migrated DBs) ──

        private static void MigrateLegacySchema(SqliteConnection conn)
        {
            var uiFormsNeedsRebuild = TableExists(conn, "ui_forms") && !TableHasColumn(conn, "ui_forms", "version");
            // Legacy schema_definitions had a single-column PK (name); the versioned layout is composite.
            var schemaDefsNeedRebuild = TableExists(conn, "schema_definitions") && PrimaryKeyColumnCount(conn, "schema_definitions") == 1;
            // The legacy attribute_domains carried an FK to schema_definitions(name) — invalid once schemas
            // are multi-version (the referenced column is no longer unique), so the table is rebuilt without it.
            var domainsNeedRebuild = TableExists(conn, "attribute_domains") && !TableHasColumn(conn, "attribute_domains", "schema_definition_version");

            if (!uiFormsNeedsRebuild && !schemaDefsNeedRebuild && !domainsNeedRebuild) return;

            // Rebuilding tables with cross-FKs requires foreign_keys=OFF. The pragma is a no-op inside a
            // transaction, so it must be toggled before BEGIN and restored after COMMIT.
            using (var off = conn.CreateCommand())
            {
                off.CommandText = "PRAGMA foreign_keys = OFF;";
                off.ExecuteNonQuery();
            }

            try
            {
                using var tx = conn.BeginTransaction();

                if (uiFormsNeedsRebuild)
                    Execute(conn, tx, """
                        CREATE TABLE ui_forms_new (
                          form_id TEXT NOT NULL, version TEXT NOT NULL, title TEXT, description TEXT,
                          attribute_domain_name TEXT, schema_definition_name TEXT, page_json TEXT NOT NULL,
                          is_current_version INTEGER NOT NULL DEFAULT 1, updated_at_utc TEXT,
                          PRIMARY KEY(form_id, version));
                        INSERT INTO ui_forms_new (form_id, version, title, description, attribute_domain_name, schema_definition_name, page_json, is_current_version, updated_at_utc)
                          SELECT form_id, '1', title, description, attribute_domain_name, schema_definition_name, page_json, 1, updated_at_utc FROM ui_forms;
                        DROP TABLE ui_forms;
                        ALTER TABLE ui_forms_new RENAME TO ui_forms;
                        """);

                if (schemaDefsNeedRebuild)
                    Execute(conn, tx, """
                        CREATE TABLE schema_definitions_new (
                          schema_definition_name TEXT NOT NULL, version TEXT NOT NULL, description TEXT,
                          definition TEXT, definition_from TEXT, is_attribute_domain INTEGER NOT NULL DEFAULT 0,
                          created_by TEXT, created_on TEXT, PRIMARY KEY(schema_definition_name, version));
                        INSERT INTO schema_definitions_new (schema_definition_name, version, description, definition, definition_from, is_attribute_domain, created_by, created_on)
                          SELECT schema_definition_name, COALESCE(NULLIF(version, ''), '1'), description, definition, definition_from, is_attribute_domain, created_by, created_on FROM schema_definitions;
                        DROP TABLE schema_definitions;
                        ALTER TABLE schema_definitions_new RENAME TO schema_definitions;
                        """);

                if (domainsNeedRebuild)
                    Execute(conn, tx, """
                        CREATE TABLE attribute_domains_new (
                          domain_name TEXT PRIMARY KEY, version TEXT, description TEXT,
                          schema_definition_name TEXT, schema_definition_version TEXT,
                          is_current_version INTEGER NOT NULL DEFAULT 1, supersedes_domain_id INTEGER,
                          created_at_utc TEXT);
                        INSERT INTO attribute_domains_new (domain_name, version, description, schema_definition_name, schema_definition_version, is_current_version, supersedes_domain_id, created_at_utc)
                          SELECT domain_name, version, description, schema_definition_name,
                                 (SELECT sd.version FROM schema_definitions sd WHERE sd.schema_definition_name = attribute_domains.schema_definition_name LIMIT 1),
                                 is_current_version, supersedes_domain_id, created_at_utc
                          FROM attribute_domains;
                        DROP TABLE attribute_domains;
                        ALTER TABLE attribute_domains_new RENAME TO attribute_domains;
                        """);

                tx.Commit();
            }
            finally
            {
                using var on = conn.CreateCommand();
                on.CommandText = "PRAGMA foreign_keys = ON;";
                on.ExecuteNonQuery();
            }
        }

        private static void Execute(SqliteConnection conn, SqliteTransaction tx, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static bool TableExists(SqliteConnection conn, string table) => TableColumns(conn, table).Count > 0;

        private static bool TableHasColumn(SqliteConnection conn, string table, string column) =>
            TableColumns(conn, table).Any(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase));

        private static List<string> TableColumns(SqliteConnection conn, string table)
        {
            var columns = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                columns.Add(reader.GetString(1)); // column name is field 1
            return columns;
        }

        private static int PrimaryKeyColumnCount(SqliteConnection conn, string table)
        {
            string? pkIndexName = null;
            using (var cmd = conn.CreateCommand())
            {
                // PRAGMA index_list columns: 0=seq, 1=name, 2=unique(0/1), 3=origin('pk'|'u'|'c'|'k'), 4=partial.
                cmd.CommandText = $"PRAGMA index_list({table});";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    if (string.Equals(reader.GetString(3), "pk", StringComparison.OrdinalIgnoreCase))
                        pkIndexName = reader.GetString(1);
            }

            if (pkIndexName == null) return 0; // rowid table without an explicit PK index

            using var info = conn.CreateCommand();
            info.CommandText = $"PRAGMA index_info({pkIndexName});";
            using var infoReader = info.ExecuteReader();
            var count = 0;
            while (infoReader.Read()) count++;
            return count;
        }
    }
}
