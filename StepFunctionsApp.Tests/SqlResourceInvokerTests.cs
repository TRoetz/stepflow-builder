using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// sql:// handler: SELECT → { rows, count }, DML → { changes }, missing query/db errors,
    /// and remote connection string rejection (local SQLite files only).
    /// </summary>
    public class SqlResourceInvokerTests : IDisposable
    {
        private readonly CompositeResourceInvoker _invoker;
        private readonly string _dir;
        private readonly string _dbPath;
        private readonly string _eavDir;

        public SqlResourceInvokerTests()
        {
            (_invoker, _, _eavDir) = TestResourceInvokers.Create();
            _dir = Path.Combine(Path.GetTempPath(), "stepflow-sql-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _dbPath = Path.Combine(_dir, "test.db");

            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT); INSERT INTO t (name) VALUES ('alpha'), ('beta');";
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(_eavDir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public async Task Select_ReturnsRowsAndCount()
        {
            var result = await _invoker.InvokeAsync($"sql://{_dbPath}", new JObject { ["query"] = "SELECT * FROM t ORDER BY id" }, CancellationToken.None);
            var obj = (JObject)result;
            Assert.Equal(2, (int)obj["count"]!);
            var rows = (JArray)obj["rows"]!;
            Assert.Equal("alpha", (string?)rows[0]["name"]);
            Assert.Equal(1L, (long)rows[0]["id"]!);
        }

        [Fact]
        public async Task Dml_ReturnsChanges()
        {
            var result = await _invoker.InvokeAsync($"sql://{_dbPath}", new JObject { ["query"] = "INSERT INTO t (name) VALUES ('gamma')" }, CancellationToken.None);
            Assert.Equal(1, (int)((JObject)result)["changes"]!);
        }

        [Fact]
        public async Task MissingQuery_Fails()
        {
            var ex = await Assert.ThrowsAsync<StepEngineException>(() => _invoker.InvokeAsync($"sql://{_dbPath}", new JObject(), CancellationToken.None));
            Assert.Equal("States.TaskFailed", ex.ErrorCode);
            Assert.Contains("'query'", ex.Message);
        }

        [Fact]
        public async Task MissingDbFile_Fails()
        {
            var missing = Path.Combine(_dir, "nope.db");
            var ex = await Assert.ThrowsAsync<StepEngineException>(() => _invoker.InvokeAsync($"sql://{missing}", new JObject { ["query"] = "SELECT 1" }, CancellationToken.None));
            Assert.Equal("States.TaskFailed", ex.ErrorCode);
            Assert.Contains("not found", ex.Message);
        }

        [Fact]
        public async Task RemoteConnectionString_Fails()
        {
            var ex = await Assert.ThrowsAsync<StepEngineException>(() => _invoker.InvokeAsync(
                "sql://Server=localhost;Database=x", new JObject { ["query"] = "SELECT 1" }, CancellationToken.None));
            Assert.Equal("States.TaskFailed", ex.ErrorCode);
            Assert.Contains("remote connection strings are not supported", ex.Message);
        }

        [Fact]
        public async Task DataSourceForm_Resolves()
        {
            var result = await _invoker.InvokeAsync($"sql://Data Source={_dbPath}", new JObject { ["query"] = "SELECT COUNT(*) AS n FROM t" }, CancellationToken.None);
            var obj = (JObject)result;
            Assert.Equal(1, (int)obj["count"]!); // one aggregate row
            Assert.Equal(2, (int)((JArray)obj["rows"]!)[0]["n"]!);
        }

        [Fact]
        public async Task ConnectionStringParameter_WinsOverUri()
        {
            var result = await _invoker.InvokeAsync("sql://ignored.db", new JObject
            {
                ["query"] = "SELECT COUNT(*) AS n FROM t",
                ["connectionString"] = _dbPath
            }, CancellationToken.None);
            var obj = (JObject)result;
            Assert.Equal(1, (int)obj["count"]!); // one aggregate row
            Assert.Equal(2, (int)((JArray)obj["rows"]!)[0]["n"]!);
        }
    }
}
