using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StepFunctionsApp.StepFunctions
{
    /// <summary>
    /// Resolves sql:// resource URIs to local SQLite file paths.
    /// Resolution order:
    ///  1. Literal path (plain relative/absolute, "file:" URI, or "Data Source=..." connection string) —
    ///     wins when the file exists, so existing flow definitions keep working unchanged.
    ///  2. Logical datasource name from the appsettings "SqlDataSources" section (env-var expansion
    ///     supported: %NAME% and ${NAME}). This is what makes flow definitions portable across
    ///     environments — a flow can reference sql://fees while each environment binds "fees" to its own file.
    /// </summary>
    public static class SqlDataSourceResolver
    {
        private static readonly Regex EnvVarPattern = new(@"%([A-Za-z_][A-Za-z0-9_]*)%|\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        /// <summary>Expands %VAR% and ${VAR} environment variable references; unknown vars expand to empty string.</summary>
        public static string ExpandEnvironmentVariables(string value) =>
            EnvVarPattern.Replace(value, m =>
            {
                var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                return Environment.GetEnvironmentVariable(name) ?? "";
            });

        /// <summary>
        /// The raw segment after "sql://" — the literal path or logical datasource name used in flow definitions.
        /// </summary>
        public static string LogicalName(string resource) =>
            resource["sql://".Length..].Trim().TrimEnd('/');

        /// <summary>
        /// Resolves a sql:// connection string to an existing local SQLite file path (throws StepEngineException when unresolvable).
        /// </summary>
        public static string Resolve(string connectionString, IReadOnlyDictionary<string, string>? sources) =>
            Resolve(connectionString, sources, allowCreate: false);

        /// <summary>
        /// Same as <see cref="Resolve(string, IReadOnlyDictionary{string,string})"/> but with allowCreate for solution import:
        /// when a logical datasource maps to a path that does not exist yet, the parent directory is created and the
        /// SQLite file will be created on first connect (fresh prod databases start empty; migrations create the schema).
        /// Literal paths never get created — only configured logical names may.
        /// </summary>
        public static string Resolve(string connectionString, IReadOnlyDictionary<string, string>? sources, bool allowCreate)
        {
            var cs = connectionString.Trim();

            // Normalize "file:" URIs and "Data Source=..." connection strings down to a plain path.
            if (cs.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var path = cs["file:".Length..];
                var cut = path.IndexOfAny(new[] { '?', '#' });
                if (cut >= 0) path = path[..cut];
                if (path.StartsWith("//"))
                {
                    // Strip the URI authority slashes: "file:///C:/x.db" -> "C:/x.db" (Windows drive paths
                    // are absolute as-is), "file:///var/x.db" -> "/var/x.db" (POSIX needs its root slash back).
                    var stripped = path.TrimStart('/');
                    cs = Regex.IsMatch(stripped, @"^[A-Za-z]:[\\/]") ? stripped : "/" + stripped;
                }
                else
                {
                    cs = path; // already a plain absolute path (e.g. "file:/var/x.db")
                }
            }
            else if (cs.Contains('='))
            {
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

            // 1. Literal path wins when the file exists (backward compatible with pre-datasource flow definitions).
            var literalPath = Path.GetFullPath(cs);
            if (File.Exists(literalPath)) return literalPath;

            // 2. Logical datasource name from configuration.
            if (sources != null && sources.TryGetValue(cs, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            {
                var expanded = ExpandEnvironmentVariables(mapped).Trim();
                if (!string.IsNullOrEmpty(expanded))
                {
                    var fullPath = Path.GetFullPath(expanded);
                    if (File.Exists(fullPath)) return fullPath;
                    if (allowCreate)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                        return fullPath; // SQLite creates the file on first connect.
                    }
                }
            }

            var known = sources is { Count: > 0 } ? string.Join(", ", sources.Keys.OrderBy(k => k, StringComparer.Ordinal)) : "(none configured)";
            throw new StepEngineException("States.TaskFailed",
                $"SQLite database not found for sql://{cs}. Looked for literal file '{literalPath}' and logical datasource '{cs}'. Configured datasources: {known}");
        }
    }
}
