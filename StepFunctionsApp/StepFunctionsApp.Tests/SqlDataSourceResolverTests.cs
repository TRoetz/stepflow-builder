using System;
using System.Collections.Generic;
using System.IO;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// sql:// datasource resolution: literal paths win when the file exists (backward compatible),
    /// logical names fall back to the SqlDataSources configuration map with env-var expansion.
    /// </summary>
    public sealed class SqlDataSourceResolverTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "stepflow-sql-resolver-tests", Guid.NewGuid().ToString("N"));

        public SqlDataSourceResolverTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string TouchFile(string name)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, "");
            return path;
        }

        [Fact]
        public void Literal_existing_file_resolves_directly()
        {
            var path = TouchFile("fees.db");
            Assert.Equal(Path.GetFullPath(path), SqlDataSourceResolver.Resolve(path, null));
        }

        [Fact]
        public void Logical_name_falls_back_to_configured_map()
        {
            var path = TouchFile("real-fees.db");
            var sources = new Dictionary<string, string> { ["fees"] = path };
            Assert.Equal(Path.GetFullPath(path), SqlDataSourceResolver.Resolve("fees", sources));
        }

        [Fact]
        public void Literal_path_wins_over_map_when_file_exists()
        {
            var literal = TouchFile("literal.db");
            var mapped = TouchFile("mapped.db");
            // Map says "literal" -> mapped.db, but the literal file exists, so it wins.
            var sources = new Dictionary<string, string> { ["literal"] = mapped };
            Assert.Equal(Path.GetFullPath(literal), SqlDataSourceResolver.Resolve(literal, sources));
        }

        [Fact]
        public void Env_vars_expand_in_percent_and_brace_forms()
        {
            var path = TouchFile("env-fees.db");
            Environment.SetEnvironmentVariable("STEPFLOW_TEST_SQL_A", path);
            Environment.SetEnvironmentVariable("STEPFLOW_TEST_SQL_B", path);
            try
            {
                var sourcesPct = new Dictionary<string, string> { ["fees"] = "%STEPFLOW_TEST_SQL_A%" };
                var sourcesBrace = new Dictionary<string, string> { ["fees2"] = "${STEPFLOW_TEST_SQL_B}" };
                Assert.Equal(Path.GetFullPath(path), SqlDataSourceResolver.Resolve("fees", sourcesPct));
                Assert.Equal(Path.GetFullPath(path), SqlDataSourceResolver.Resolve("fees2", sourcesBrace));
            }
            finally
            {
                Environment.SetEnvironmentVariable("STEPFLOW_TEST_SQL_A", null);
                Environment.SetEnvironmentVariable("STEPFLOW_TEST_SQL_B", null);
            }
        }

        [Fact]
        public void Data_source_connection_string_form_resolves()
        {
            var path = TouchFile("cs.db");
            Assert.Equal(Path.GetFullPath(path), SqlDataSourceResolver.Resolve($"Data Source={path}", null));
        }

        [Fact]
        public void File_uri_form_resolves()
        {
            var path = TouchFile("uri.db");
            var uri = "file://" + path.Replace('\\', '/');
            Assert.Equal(Path.GetFullPath(path), SqlDataSourceResolver.Resolve(uri, null));
        }

        [Fact]
        public void Unknown_name_throws_with_configured_names_listed()
        {
            TouchFile("other.db");
            var sources = new Dictionary<string, string> { ["fees"] = Path.Combine(_dir, "missing.db") };
            var ex = Assert.Throws<StepEngineException>(() => SqlDataSourceResolver.Resolve("nope", sources));
            Assert.Contains("nope", ex.Message);
            Assert.Contains("fees", ex.Message); // known names are listed to help debugging
        }

        [Fact]
        public void Allow_create_makes_parent_dirs_for_logical_names_only()
        {
            var target = Path.Combine(_dir, "new", "sub", "prod-fees.db");
            var sources = new Dictionary<string, string> { ["fees"] = target };

            // Runtime resolution (allowCreate=false) refuses to create.
            Assert.Throws<StepEngineException>(() => SqlDataSourceResolver.Resolve("fees", sources));

            // Import resolution creates the parent directory and returns the path (SQLite creates the file on connect).
            var resolved = SqlDataSourceResolver.Resolve("fees", sources, allowCreate: true);
            Assert.Equal(Path.GetFullPath(target), resolved);
            Assert.True(Directory.Exists(Path.GetDirectoryName(resolved)!));
        }

        [Fact]
        public void Logical_name_strips_trailing_slash()
        {
            Assert.Equal("fees", SqlDataSourceResolver.LogicalName("sql://fees"));
            Assert.Equal("C:/data/fees.db", SqlDataSourceResolver.LogicalName("sql://C:/data/fees.db/"));
        }
    }
}
