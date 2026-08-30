using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using StepFlow.DynamicApi;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.DynamicApi;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// SQLITE DYNAMIC API STORE - one row per user-defined REST API in the shared
// stepflow_data.db (dynamic_apis table); operations live in a single JSON column.
// Readers run sequentially on one connection (no MARS in Microsoft.Data.Sqlite).
// GetAll returns active rows only; GetById sees every row so inactive APIs can be
// fetched by id and re-saved as active.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

public sealed class SqliteDynamicApiStore : IDynamicApiStore, IDisposable
{
    private const string StoreDdl = """
        CREATE TABLE IF NOT EXISTS dynamic_apis (
          api_id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          description TEXT NOT NULL DEFAULT '',
          node_path TEXT NOT NULL,
          base_path TEXT NOT NULL,
          attribute_domain TEXT,
          bearer_token TEXT,
          is_active INTEGER NOT NULL DEFAULT 1,
          is_published INTEGER NOT NULL DEFAULT 0,
          operations_json TEXT NOT NULL,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_dynamic_apis_node ON dynamic_apis(node_path);
        """;

    private const string Columns = "api_id, name, description, node_path, base_path, attribute_domain, bearer_token, is_active, is_published, operations_json, created_at, updated_at";

    private static readonly JsonSerializerSettings OpsJson = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };

    private readonly string _dbPath;
    private readonly ILogger<SqliteDynamicApiStore>? _logger;
    private readonly object _lock = new();

    public SqliteDynamicApiStore(string dbPath, ILogger<SqliteDynamicApiStore>? logger = null)
    {
        _dbPath = Path.GetFullPath(dbPath);
        _logger = logger;
        using var conn = StepFlowDataDb.Open(_dbPath);
        StepFlowDataDb.EnsureSchema(conn);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = StoreDdl;
            cmd.ExecuteNonQuery();
        }

        // Idempotent migration for databases created before the published flag existed.
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(dynamic_apis);";
            bool hasPublished = false;
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader.GetString(1), "is_published", StringComparison.OrdinalIgnoreCase))
                    hasPublished = true;
            if (!hasPublished)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE dynamic_apis ADD COLUMN is_published INTEGER NOT NULL DEFAULT 0;";
                alter.ExecuteNonQuery();
                _logger?.LogInformation("Migrated dynamic_apis table: added is_published column (existing APIs default to unpublished)");
            }
        }
    }

    public IReadOnlyList<DynamicApiDefinition> GetAll(string? nodePathPrefix = null)
    {
        lock (_lock)
        {
            var result = new List<DynamicApiDefinition>();
            using var conn = StepFlowDataDb.Open(_dbPath);
            using var cmd = conn.CreateCommand();
            if (string.IsNullOrWhiteSpace(nodePathPrefix))
                cmd.CommandText = $"SELECT {Columns} FROM dynamic_apis WHERE is_active = 1 ORDER BY api_id;";
            else
            {
                // '/'-segment aware prefix: the node itself or anything nested under it.
                cmd.CommandText = $"SELECT {Columns} FROM dynamic_apis WHERE is_active = 1 AND (node_path = $p OR node_path LIKE $p || '/%') ORDER BY api_id;";
                cmd.Parameters.AddWithValue("$p", nodePathPrefix.Trim('/'));
            }
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add(ReadRow(r));
            return result;
        }
    }

    public DynamicApiDefinition? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_lock)
        {
            using var conn = StepFlowDataDb.Open(_dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM dynamic_apis WHERE api_id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadRow(r) : null;
        }
    }

    public (string Id, bool Created) Save(DynamicApiDefinition def)
    {
        if (def == null) throw new ArgumentNullException(nameof(def));
        lock (_lock)
        {
            using var conn = StepFlowDataDb.Open(_dbPath);
            string? targetId = null;

            // 1. Explicit id that already exists -> update in place, preserving CreatedAt.
            var requested = SanitizeId(def.Id);
            if (requested.Length > 0)
                targetId = ReadRowById(conn, requested)?.Id;

            // 2. Same Name + NodePath -> reuse its Id (case-insensitive name).
            if (targetId == null && !string.IsNullOrWhiteSpace(def.Name))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT api_id FROM dynamic_apis WHERE name = $n COLLATE NOCASE AND node_path = $p LIMIT 1;";
                cmd.Parameters.AddWithValue("$n", def.Name);
                cmd.Parameters.AddWithValue("$p", def.NodePath ?? "");
                targetId = cmd.ExecuteScalar() as string;
            }

            bool created;
            DateTime createdAt;
            if (targetId != null)
            {
                var existing = ReadRowById(conn, targetId)!;
                createdAt = existing.CreatedAt;
                created = false;
            }
            else
            {
                targetId = UniquifyId(conn, requested.Length > 0 ? requested : Slug(def.Name));
                createdAt = DateTime.UtcNow;
                created = true;
            }

            def.Id = targetId;
            if (created) def.CreatedAt = createdAt;
            def.UpdatedAt = DateTime.UtcNow;

            using var upsert = conn.CreateCommand();
            upsert.CommandText = """
                INSERT INTO dynamic_apis (api_id, name, description, node_path, base_path, attribute_domain, bearer_token, is_active, is_published, operations_json, created_at, updated_at)
                VALUES ($id, $name, $desc, $node, $base, $domain, $token, $active, $published, $ops, $created, $updated)
                ON CONFLICT(api_id) DO UPDATE SET
                  name = excluded.name,
                  description = excluded.description,
                  node_path = excluded.node_path,
                  base_path = excluded.base_path,
                  attribute_domain = excluded.attribute_domain,
                  bearer_token = excluded.bearer_token,
                  is_active = excluded.is_active,
                  is_published = excluded.is_published,
                  operations_json = excluded.operations_json,
                  updated_at = excluded.updated_at;
                """;
            upsert.Parameters.AddWithValue("$id", targetId);
            upsert.Parameters.AddWithValue("$name", def.Name ?? "");
            upsert.Parameters.AddWithValue("$desc", def.Description ?? "");
            upsert.Parameters.AddWithValue("$node", def.NodePath ?? "");
            upsert.Parameters.AddWithValue("$base", string.IsNullOrWhiteSpace(def.BasePath) ? "/" : def.BasePath);
            upsert.Parameters.AddWithValue("$domain", (object?)def.AttributeDomain ?? DBNull.Value);
            upsert.Parameters.AddWithValue("$token", (object?)def.BearerToken ?? DBNull.Value);
            upsert.Parameters.AddWithValue("$active", def.IsActive ? 1L : 0L);
            upsert.Parameters.AddWithValue("$published", def.IsPublished ? 1L : 0L);
            upsert.Parameters.AddWithValue("$ops", JsonConvert.SerializeObject(def.Operations ?? new List<DynamicApiOperation>(), OpsJson));
            upsert.Parameters.AddWithValue("$created", createdAt.ToString("o", CultureInfo.InvariantCulture));
            upsert.Parameters.AddWithValue("$updated", def.UpdatedAt.ToString("o", CultureInfo.InvariantCulture));
            upsert.ExecuteNonQuery();

            _logger?.LogInformation("Saved dynamic API {Id} ({Name}) under node {NodePath}: {Created}", targetId, def.Name, def.NodePath, created ? "created" : "updated");
            return (targetId, created);
        }
    }

    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_lock)
        {
            using var conn = StepFlowDataDb.Open(_dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM dynamic_apis WHERE api_id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public void Dispose() { }

    private static DynamicApiDefinition ReadRow(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Name = r.GetString(1),
        Description = r.GetString(2),
        NodePath = r.GetString(3),
        BasePath = r.GetString(4),
        AttributeDomain = r.IsDBNull(5) ? null : r.GetString(5),
        BearerToken = r.IsDBNull(6) ? null : r.GetString(6),
        IsActive = r.GetInt64(7) != 0,
        IsPublished = r.GetInt64(8) != 0,
        Operations = ParseOperations(r.GetString(9)),
        CreatedAt = DateTime.Parse(r.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTime.Parse(r.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
    };

    private static DynamicApiDefinition? ReadRowById(SqliteConnection conn, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM dynamic_apis WHERE api_id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRow(r) : null;
    }

    private static List<DynamicApiOperation> ParseOperations(string json) =>
        JsonConvert.DeserializeObject<List<DynamicApiOperation>>(json, OpsJson) ?? new List<DynamicApiOperation>();

    /// <summary>New Id: the base, or base-2, base-3, ... until unused.</summary>
    private static string UniquifyId(SqliteConnection conn, string baseId)
    {
        var candidate = baseId.Length > 0 ? baseId : "api";
        for (int i = 2; ; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM dynamic_apis WHERE api_id = $id;";
            cmd.Parameters.AddWithValue("$id", candidate);
            if (cmd.ExecuteScalar() == null) return candidate;
            candidate = $"{baseId}-{i}";
        }
    }

    private static string SanitizeId(string? value) =>
        Regex.Replace(value ?? "", @"[^A-Za-z0-9_-]", "-").Trim('-');

    private static string Slug(string name)
    {
        var s = SanitizeId(name);
        return s.Length > 0 ? s : "api";
    }
}
