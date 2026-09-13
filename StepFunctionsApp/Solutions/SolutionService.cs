using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities.DataSource;
using StepFlow.DataModel.Entities.MetaData;
using StepFlow.DynamicApi;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.DynamicApi;
using StepFunctionsApp.StepFunctions;
using StepFunctionsApp.Workspace;

namespace StepFunctionsApp.Solutions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SOLUTION PACKAGING — export a workspace node's application (flows + forms +
    // attribute domains + dynamic APIs + data-exchange profiles + data-source schemas)
    // into one portable JSON package, and import it onto another instance. This is the
    // test -> prod deploy path:
    //   1. export_solution on the source instance  →  council-fees.solution.json
    //   2. configure SqlDataSources bindings on the target (appsettings / env vars)
    //   3. import_solution on the target           →  flows registered, metadata upserted,
    //                                                 data-source migrations applied
    // Flow definitions reference datasources by logical name (sql://fees), so the same
    // package deploys unchanged to any environment.
    // ═══════════════════════════════════════════════════════════════════════════════

    public sealed class SolutionManifest
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1";
        public string? Description { get; set; }
        public string CreatedAtUtc { get; set; } = DateTime.UtcNow.ToString("o");
        /// <summary>Workspace node the package was exported from (default import target).</summary>
        public string SourceNodePath { get; set; } = "";
    }

    public sealed class SolutionFlow
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string StartAt { get; set; } = "";
        /// <summary>Amazon States Language states object (camelCase, UI-compatible).</summary>
        public JObject States { get; set; } = new();
    }

    public sealed class SolutionAttribute
    {
        public string AttributeName { get; set; } = "";
        public string DataType { get; set; } = "String"; // AttributeDataType enum name
        public string Description { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Placeholder { get; set; } = "";
        public string HelpText { get; set; } = "";
        public bool Visible { get; set; } = true;
        public bool ReadOnly { get; set; }
        public bool PrimaryKey { get; set; }
        public JToken? ValidationSchemaJson { get; set; }
    }

    public sealed class SolutionDomain
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1";
        public string Description { get; set; } = "";
        /// <summary>List order is the display sort order.</summary>
        public List<SolutionAttribute> Attributes { get; set; } = new();
        /// <summary>Linked schema definition, when the domain pins one (exported with its full body).</summary>
        public SolutionSchemaDefinition? SchemaDefinition { get; set; }
    }

    public sealed class SolutionSchemaDefinition
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1";
        public string Description { get; set; } = "";
        /// <summary>The schema definition JSON body.</summary>
        public JToken Definition { get; set; } = new JObject();
    }

    public sealed class SolutionForm
    {
        public string FormId { get; set; } = "";
        public string Version { get; set; } = "1";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? AttributeDomainName { get; set; }
        /// <summary>Full UIPage JSON (uidata-schema.json).</summary>
        public JToken Page { get; set; } = new JObject();
    }

    public sealed class SolutionApi
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string BasePath { get; set; } = "/";
        public string? AttributeDomain { get; set; }
        /// <summary>Set for prod deploys: requests must carry "Authorization: Bearer &lt;token&gt;".</summary>
        public string? BearerToken { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsPublished { get; set; }
        public List<DynamicApiOperation> Operations { get; set; } = new();
    }

    public sealed class SolutionDataSource
    {
        /// <summary>Logical name as referenced by flows (sql://&lt;name&gt;) and bound via appsettings SqlDataSources.</summary>
        public string Name { get; set; } = "";
        /// <summary>Ordered SQL scripts applied at import time. Idempotent by design (IF NOT EXISTS / OR IGNORE).</summary>
        public List<string> Migrations { get; set; } = new();
    }

    public sealed class SolutionDataExchangeProfile
    {
        /// <summary>Resolved profile id (dataexchange://&lt;id&gt; URI identity).</summary>
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>The full DataExchangeProfile document, round-tripped as JSON.</summary>
        public JToken Document { get; set; } = new JObject();
    }

    public sealed class SolutionPackage
    {
        public string Format => "stepflow-solution";
        public int FormatVersion => 1;
        public SolutionManifest Manifest { get; set; } = new();
        public List<SolutionFlow> Flows { get; set; } = new();
        public List<SolutionDomain> AttributeDomains { get; set; } = new();
        public List<SolutionSchemaDefinition> SchemaDefinitions { get; set; } = new();
        public List<SolutionForm> Forms { get; set; } = new();
        public List<SolutionApi> DynamicApis { get; set; } = new();
        public List<SolutionDataExchangeProfile> DataExchangeProfiles { get; set; } = new();
        public List<SolutionDataSource> DataSources { get; set; } = new();
    }

    public sealed class SolutionService
    {
        private readonly WorkspaceStore _workspace;
        private readonly Lazy<StepFunctionService> _stepService;
        private readonly IFormDefinitionStore _forms;
        private readonly IAttributeDomainStore _domains;
        private readonly ISchemaDefinitionStore _schemas;
        private readonly IDynamicApiStore _apis;
        private readonly DataExchangeProfileStore _dxProfiles;
        private readonly IReadOnlyDictionary<string, string>? _sqlDataSources;

        public SolutionService(
            WorkspaceStore workspace,
            Lazy<StepFunctionService> stepService,
            IFormDefinitionStore forms,
            IAttributeDomainStore domains,
            ISchemaDefinitionStore schemas,
            IDynamicApiStore apis,
            DataExchangeProfileStore dxProfiles,
            IConfiguration configuration)
        {
            _workspace = workspace;
            _stepService = stepService;
            _forms = forms;
            _domains = domains;
            _schemas = schemas;
            _apis = apis;
            _dxProfiles = dxProfiles;
            _sqlDataSources = configuration.GetSection("SqlDataSources").GetChildren()
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.OrdinalIgnoreCase);
        }

        // ── EXPORT ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Exports the application attached to a workspace node: its flows (plus every form and
        /// attribute domain they reference), the node's dynamic APIs, the node's data-exchange
        /// profiles, and the schema of every sql:// datasource the flows use. seedTables adds
        /// INSERT OR IGNORE row dumps for reference tables.
        /// </summary>
        public SolutionPackage Export(string nodePath, IEnumerable<string>? seedTables = null, string? name = null, string? version = null)
        {
            if (!_workspace.NodeExists(nodePath))
                throw new ArgumentException($"Workspace node '{nodePath}' does not exist.", nameof(nodePath));

            var seed = (seedTables ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Default package identity: the sub-project name (last path segment), version 1.
            var defaultName = nodePath.Split('/').LastOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "solution";
            var pkg = new SolutionPackage { Manifest = new SolutionManifest
            {
                Name = string.IsNullOrWhiteSpace(name) ? defaultName : name!.Trim(),
                Version = string.IsNullOrWhiteSpace(version) ? "1" : version!.Trim(),
                SourceNodePath = nodePath
            } };
            var formIds = new List<string>();
            var sqlNames = new List<string>();

            foreach (var (id, meta) in _workspace.ListFlows(nodePath))
            {
                var defJson = _workspace.LoadFlowDefinition(nodePath, id);
                if (defJson == null) continue;
                var doc = JObject.Parse(defJson);
                var states = doc["states"] as JObject ?? new JObject();

                pkg.Flows.Add(new SolutionFlow
                {
                    Name = meta.Name,
                    Description = string.IsNullOrWhiteSpace(meta.Description) ? null : meta.Description,
                    StartAt = (string?)doc["startAt"] ?? "",
                    States = states
                });

                foreach (var prop in states.Properties())
                {
                    var state = prop.Value as JObject;
                    if (state == null) continue;
                    if ((string?)state["type"] == "FormCapture")
                    {
                        var fid = (string?)state?["task"]?["formId"];
                        if (!string.IsNullOrWhiteSpace(fid) && !formIds.Contains(fid!)) formIds.Add(fid!);
                    }
                    var resource = (string?)state?["resource"];
                    if (resource != null && resource.StartsWith("sql://", StringComparison.OrdinalIgnoreCase))
                    {
                        var srcName = SqlDataSourceResolver.LogicalName(resource);
                        if (!sqlNames.Contains(srcName)) sqlNames.Add(srcName);
                    }
                }
            }

            // Forms referenced by the flows → their bound attribute domains.
            var domainNames = new List<string>();
            foreach (var fid in formIds)
            {
                var form = _forms.Get(fid);
                if (form == null) continue;
                pkg.Forms.Add(new SolutionForm
                {
                    FormId = form.FormId,
                    Version = form.Version,
                    Title = form.Title,
                    Description = form.Description,
                    AttributeDomainName = form.AttributeDomainName,
                    Page = form.Page
                });
                if (!string.IsNullOrWhiteSpace(form.AttributeDomainName) && !domainNames.Contains(form.AttributeDomainName!))
                    domainNames.Add(form.AttributeDomainName!);
            }

            foreach (var dn in domainNames)
            {
                var (domain, schema) = _domains.GetByName(dn);
                if (domain == null) continue;
                pkg.AttributeDomains.Add(new SolutionDomain
                {
                    Name = domain.AttributeDomainName,
                    Version = string.IsNullOrWhiteSpace(domain.Version) ? "1" : domain.Version,
                    Description = domain.Description ?? "",
                    Attributes = domain.Attributes.Select(a => new SolutionAttribute
                    {
                        AttributeName = a.AttributeName,
                        DataType = a.DataType.ToString(),
                        Description = a.Description ?? "",
                        DisplayName = a.DisplayName ?? "",
                        Placeholder = a.Placeholder ?? "",
                        HelpText = a.HelpText ?? "",
                        Visible = a.Visible,
                        ReadOnly = a.ReadOnly,
                        PrimaryKey = a.PrimaryKey,
                        ValidationSchemaJson = string.IsNullOrWhiteSpace(a.ValidationSchemaJson) ? null : JToken.Parse(a.ValidationSchemaJson!)
                    }).ToList(),
                    SchemaDefinition = schema != null && !string.IsNullOrWhiteSpace(schema.Definition)
                        ? new SolutionSchemaDefinition
                        {
                            Name = schema.SchemaDefinitionName,
                            Version = string.IsNullOrWhiteSpace(schema.Version) ? "1" : schema.Version,
                            Description = schema.Description ?? "",
                            Definition = JToken.Parse(schema.Definition!)
                        }
                        : null
                });
            }

            // Data-exchange profiles located under this node (same rule as dynamic APIs).
            foreach (var entry in _dxProfiles.ScanAll())
            {
                if (!string.Equals(entry.SubProjectPath, nodePath, StringComparison.OrdinalIgnoreCase)) continue;
                var id = DataExchangeProfileStore.ResolveId(entry.Profile);
                pkg.DataExchangeProfiles.Add(new SolutionDataExchangeProfile
                {
                    Id = id,
                    Name = entry.Profile.DataExchangeProfileName ?? id,
                    Document = JToken.FromObject(entry.Profile, JsonSerializer.Create(new JsonSerializerSettings
                    {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        NullValueHandling = NullValueHandling.Ignore
                    }))
                });
            }

            // Dynamic APIs attached to this node.
            foreach (var api in _apis.GetAll(nodePath))
            {
                pkg.DynamicApis.Add(new SolutionApi
                {
                    Id = api.Id,
                    Name = api.Name,
                    Description = api.Description ?? "",
                    BasePath = api.BasePath,
                    AttributeDomain = api.AttributeDomain,
                    BearerToken = api.BearerToken,
                    IsActive = api.IsActive,
                    IsPublished = api.IsPublished,
                    Operations = api.Operations.ToList()
                });
            }

            // Data-source schemas (DDL) + optional reference-table seeds.
            foreach (var dsName in sqlNames)
            {
                string path;
                try { path = SqlDataSourceResolver.Resolve(dsName, _sqlDataSources); }
                catch (Exception ex) when (ex is StepEngineException or ArgumentException)
                {
                    throw new InvalidOperationException($"Flow references sql://{dsName} but it cannot be resolved on this instance: {ex.Message}", ex);
                }
                pkg.DataSources.Add(BuildDataSourceMigration(dsName, path, seed));
            }

            return pkg;
        }

        private static SolutionDataSource BuildDataSourceMigration(string name, string dbPath, HashSet<string> seedTables)
        {
            var migrations = new List<string>();
            using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                conn.Open();

                // DDL: tables first, then indexes (auto-indexes have NULL sql and are skipped).
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT name, sql FROM sqlite_master
                        WHERE type IN ('table','index') AND sql IS NOT NULL AND name NOT LIKE 'sqlite_%'
                        ORDER BY CASE type WHEN 'table' THEN 0 ELSE 1 END, name;
                        """;
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        migrations.Add(MakeIdempotentDdl(reader.GetString(1)));
                }

                // NOTE: DDL is normalized to IF NOT EXISTS so re-importing a package onto an
                // already-migrated database is a no-op for schema statements.

                foreach (var table in seedTables)
                {
                    if (!TableExists(conn, table)) continue;
                    var columns = new List<string>();
                    using (var colCmd = conn.CreateCommand())
                    {
                        colCmd.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)});";
                        using var cr = colCmd.ExecuteReader();
                        while (cr.Read()) columns.Add(cr.GetString(1));
                    }
                    if (columns.Count == 0) continue;

                    var rows = new List<string>();
                    using (var rowCmd = conn.CreateCommand())
                    {
                        rowCmd.CommandText = $"SELECT * FROM {QuoteIdentifier(table)};";
                        using var rr = rowCmd.ExecuteReader();
                        while (rr.Read())
                        {
                            var values = new string[rr.FieldCount];
                            for (var i = 0; i < rr.FieldCount; i++)
                                values[i] = rr.IsDBNull(i) ? "NULL" : SqlLiteral(rr.GetValue(i));
                            rows.Add($"INSERT OR IGNORE INTO {QuoteIdentifier(table)} ({string.Join(", ", columns.Select(QuoteIdentifier))}) VALUES ({string.Join(", ", values)});");
                        }
                    }
                    if (rows.Count > 0) migrations.Add(string.Join("\n", rows));
                }
            }

            return new SolutionDataSource { Name = name, Migrations = migrations };
        }

        /// <summary>
        /// Normalizes sqlite_master DDL into idempotent form: CREATE TABLE/INDEX gain IF NOT EXISTS
        /// (sqlite_master stores the original statement without it). Re-imports must not fail on existing objects.
        /// </summary>
        private static string MakeIdempotentDdl(string sql)
        {
            var trimmed = sql.Trim();
            if (Regex.IsMatch(trimmed, @"^CREATE\s+TABLE\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(trimmed, @"^CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\b", RegexOptions.IgnoreCase))
                return Regex.Replace(trimmed, @"^(CREATE\s+TABLE)\b", $"$1 IF NOT EXISTS", RegexOptions.IgnoreCase) + ";";
            if (Regex.IsMatch(trimmed, @"^CREATE\s+(UNIQUE\s+)?INDEX\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(trimmed, @"^CREATE\s+(UNIQUE\s+)?INDEX\s+IF\s+NOT\s+EXISTS\b", RegexOptions.IgnoreCase))
                return Regex.Replace(trimmed, @"^(CREATE\s+(?:UNIQUE\s+)?INDEX)\b", $"$1 IF NOT EXISTS", RegexOptions.IgnoreCase) + ";";
            return trimmed + ";";
        }

        private static bool TableExists(SqliteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t;";
            cmd.Parameters.AddWithValue("$t", table);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private static string QuoteIdentifier(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";

        private static string SqlLiteral(object value) => value switch
        {
            string s => "'" + s.Replace("'", "''") + "'",
            bool b => b ? "1" : "0",
            byte[] bytes => "X'" + Convert.ToHexString(bytes) + "'",
            DateTime dt => "'" + dt.ToString("yyyy-MM-dd HH:mm:ss") + "'",
            _ => value.ToString() ?? ""
        };

        // ── IMPORT ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Imports a solution package onto this instance: flows are saved to the target node and
        /// registered for execution; schema definitions, attribute domains, forms and dynamic APIs
        /// are upserted by identity (re-importing is an idempotent redeploy); data-source migrations
        /// run in order against each datasource's bound file (created when missing). Returns a report.
        /// </summary>
        public JObject Import(SolutionPackage pkg, string? targetNodePath = null)
        {
            if (pkg == null) throw new ArgumentNullException(nameof(pkg));
            if (string.IsNullOrWhiteSpace(pkg.Manifest.Name)) throw new ArgumentException("package.manifest.name is required.");

            var node = !string.IsNullOrWhiteSpace(targetNodePath) ? targetNodePath!.Trim() : pkg.Manifest.SourceNodePath;
            if (string.IsNullOrWhiteSpace(node))
                throw new ArgumentException("No target node: pass targetNodePath or set manifest.sourceNodePath in the package.");
            _workspace.EnsureSubProject(node);

            var report = new JObject
            {
                ["solution"] = pkg.Manifest.Name,
                ["version"] = pkg.Manifest.Version,
                ["node"] = node,
                ["flows"] = new JArray(),
                ["schemaDefinitions"] = new JArray(),
                ["attributeDomains"] = new JArray(),
                ["forms"] = new JArray(),
                ["dynamicApis"] = new JArray(),
                ["dataExchangeProfiles"] = new JArray(),
                ["dataSources"] = new JArray()
            };

            // 1. Flows — persist to the workspace node, then register with the engine under the persisted id.
            foreach (var flow in pkg.Flows)
            {
                if (string.IsNullOrWhiteSpace(flow.Name)) continue;
                var definitionDoc = new JObject { ["startAt"] = flow.StartAt, ["states"] = flow.States };
                var (id, created) = _workspace.SaveFlow(node, null, flow.Name, flow.Description, definitionDoc.ToString(Formatting.None));

                StateMachineDefinition def;
                try
                {
                    def = new StateMachineDefinition
                    {
                        StartAt = flow.StartAt,
                        States = flow.States.ToObject<Dictionary<string, StateDefinition>>()!
                    };
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Flow '{flow.Name}' has an invalid state definition: {ex.Message}", ex);
                }
                _stepService.Value.RegisterStateMachine(flow.Name, def, flow.Description, id);
                ((JArray)report["flows"]).Add(new JObject { ["name"] = flow.Name, ["id"] = id, ["created"] = created });
            }

            // 2. Schema definitions first — domains may pin them by (name, version).
            foreach (var schema in pkg.SchemaDefinitions)
            {
                if (string.IsNullOrWhiteSpace(schema.Name)) continue;
                _schemas.Save(new SchemaDefinition
                {
                    SchemaDefinitionName = schema.Name,
                    Version = string.IsNullOrWhiteSpace(schema.Version) ? "1" : schema.Version,
                    Description = schema.Description ?? "",
                    Definition = schema.Definition.ToString(Formatting.None),
                    AttributeDomain = true
                });
                ((JArray)report["schemaDefinitions"]).Add(new JObject { ["name"] = schema.Name, ["version"] = schema.Version });
            }

            // 3. Attribute domains (with their attribute contracts).
            foreach (var domain in pkg.AttributeDomains)
            {
                if (string.IsNullOrWhiteSpace(domain.Name)) continue;
                var entry = new AttributeDomain
                {
                    Version = string.IsNullOrWhiteSpace(domain.Version) ? "1" : domain.Version,
                    AttributeDomainName = domain.Name,
                    Description = domain.Description ?? "",
                    IsCurrentVersion = true,
                    Attributes = domain.Attributes.Select(a => new EntityAttribute
                    {
                        AttributeName = a.AttributeName,
                        DataType = Enum.Parse<StepFlow.DataModel.Entities.AttributeDataType>(a.DataType, ignoreCase: true),
                        Description = a.Description ?? "",
                        DisplayName = a.DisplayName ?? "",
                        Placeholder = a.Placeholder ?? "",
                        HelpText = a.HelpText ?? "",
                        Visible = a.Visible,
                        ReadOnly = a.ReadOnly,
                        PrimaryKey = a.PrimaryKey,
                        ValidationSchemaJson = a.ValidationSchemaJson?.ToString(Formatting.None) ?? ""
                    }).ToList()
                };
                var schemaRef = domain.SchemaDefinition != null
                    ? new SchemaDefinition { SchemaDefinitionName = domain.SchemaDefinition.Name, Version = string.IsNullOrWhiteSpace(domain.SchemaDefinition.Version) ? "1" : domain.SchemaDefinition.Version }
                    : null;
                _domains.Save(entry, schemaRef);
                ((JArray)report["attributeDomains"]).Add(new JObject { ["name"] = domain.Name, ["attributes"] = (long)domain.Attributes.Count });
            }

            // 4. Forms.
            foreach (var form in pkg.Forms)
            {
                if (string.IsNullOrWhiteSpace(form.FormId)) continue;
                _forms.Save(new FormDefinition
                {
                    FormId = form.FormId,
                    Version = string.IsNullOrWhiteSpace(form.Version) ? "1" : form.Version,
                    Title = form.Title ?? "",
                    Description = form.Description,
                    AttributeDomainName = form.AttributeDomainName,
                    Page = form.Page,
                    IsCurrentVersion = true
                });
                ((JArray)report["forms"]).Add(new JObject { ["formId"] = form.FormId, ["version"] = form.Version });
            }

            // 5. Dynamic APIs — upserted by id; reattached to the target node.
            foreach (var api in pkg.DynamicApis)
            {
                if (string.IsNullOrWhiteSpace(api.Name)) continue;
                var def = new DynamicApiDefinition
                {
                    Id = api.Id,
                    Name = api.Name,
                    Description = api.Description ?? "",
                    NodePath = node,
                    BasePath = string.IsNullOrWhiteSpace(api.BasePath) ? "/" : api.BasePath,
                    AttributeDomain = api.AttributeDomain,
                    BearerToken = api.BearerToken,
                    IsActive = api.IsActive,
                    IsPublished = api.IsPublished,
                    Operations = api.Operations.ToList()
                };
                var (id, created) = _apis.Save(def);
                ((JArray)report["dynamicApis"]).Add(new JObject { ["id"] = id, ["name"] = api.Name, ["created"] = created });
            }

            // 6. Data-exchange profiles — upserted under the target node's data-exchange folder.
            foreach (var dx in pkg.DataExchangeProfiles)
            {
                if (dx.Document == null || dx.Document.Type == JTokenType.Null) continue;
                var profile = dx.Document.ToObject<DataExchangeProfile>();
                if (profile == null) continue;
                var id = _dxProfiles.Save(profile, node);
                ((JArray)report["dataExchangeProfiles"]).Add(new JObject { ["id"] = id, ["name"] = profile.DataExchangeProfileName });
            }

            // 7. Data-source migrations — applied in order; the bound file is created when missing (fresh prod DB).
            foreach (var ds in pkg.DataSources)
            {
                if (string.IsNullOrWhiteSpace(ds.Name)) continue;
                var path = SqlDataSourceResolver.Resolve(ds.Name, _sqlDataSources, allowCreate: true);
                var applied = 0;
                try
                {
                    using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
                    conn.Open();
                    foreach (var script in ds.Migrations)
                    {
                        if (string.IsNullOrWhiteSpace(script)) continue;
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = script;
                        cmd.ExecuteNonQuery();
                        applied++;
                    }
                }
                catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
                {
                    throw new InvalidOperationException($"Data source '{ds.Name}' migration failed against {path}: {ex.Message}", ex);
                }
                ((JArray)report["dataSources"]).Add(new JObject { ["name"] = ds.Name, ["path"] = path, ["migrationsApplied"] = applied });
            }

            return report;
        }
    }
}
