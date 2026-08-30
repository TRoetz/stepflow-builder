using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // DURABLE FLOW STATE STORES
    // Checkpoint every running execution so a restarted process can pick up where the
    // old one died. Two interchangeable backends:
    //   disk  - one JSON file per execution under a local directory (default; zero deps)
    //   redis - key per execution + index set (shared state across multiple instances)
    // Both serialize FlowStateRecord immediately on save, so callers may keep mutating
    // the in-memory Execution afterwards.
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Configuration for the durable flow-state store (appsettings.json "FlowState" section).</summary>
    public class FlowStateOptions
    {
        public const string SectionName = "FlowState";

        /// <summary>"disk" or "redis".</summary>
        public string Provider { get; set; } = "disk";

        /// <summary>Directory for per-execution JSON files (relative paths resolve against the process working directory).</summary>
        public string DiskPath { get; set; } = "flow-state";

        /// <summary>StackExchange.Redis connection string, e.g. "localhost:6379".</summary>
        public string RedisConnection { get; set; } = "localhost:6379";

        /// <summary>Automatically resume executions found Running in the store at startup.</summary>
        public bool AutoResume { get; set; } = true;
    }

    public interface IFlowStateStore
    {
        string ProviderName { get; }

        /// <summary>Persist (upsert) a checkpoint. Throws on backend failure — callers decide how to degrade.</summary>
        Task SaveAsync(FlowStateRecord record, CancellationToken ct = default);

        /// <summary>Load one execution's latest checkpoint, or null when absent/unreadable.</summary>
        Task<FlowStateRecord?> LoadAsync(string executionId, CancellationToken ct = default);

        /// <summary>All stored executions as lightweight summaries (any status).</summary>
        Task<IReadOnlyList<FlowStateSummary>> ListAsync(CancellationToken ct = default);

        /// <summary>Remove an execution's checkpoint (no-op when absent).</summary>
        Task DeleteAsync(string executionId, CancellationToken ct = default);
    }

    internal static class FlowStateJson
    {
        // Internal wire format: plain Newtonsoft defaults. Symmetric round-trip is all we need;
        // Execution.Status serializes as a string via its StringEnumConverter attribute.
        public static string Serialize(FlowStateRecord record) => JsonConvert.SerializeObject(record);

        public static FlowStateRecord? Deserialize(string json) => JsonConvert.DeserializeObject<FlowStateRecord>(json);

        public static FlowStateSummary ToSummary(FlowStateRecord record) => new()
        {
            ExecutionId = record.ExecutionId,
            StateMachineName = record.Execution.StateMachineName,
            Status = record.Execution.Status.ToString(),
            CurrentState = record.Execution.CurrentState,
            StartedAt = record.Execution.StartedAt,
            CheckpointedAt = record.CheckpointedAt
        };
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // DISK BACKEND
    // ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One JSON file per execution under a local directory. Writes are atomic
    /// (temp file + rename) so a crash mid-write never leaves a torn checkpoint.
    /// </summary>
    public sealed class DiskFlowStateStore : IFlowStateStore
    {
        private static readonly Regex SafeId = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

        private readonly string _dir;
        private readonly ILogger? _logger;

        public DiskFlowStateStore(string directory, ILogger<DiskFlowStateStore>? logger = null)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Directory is required", nameof(directory));
            _dir = Path.GetFullPath(directory);
            Directory.CreateDirectory(_dir);
            _logger = logger;
        }

        public string ProviderName => "disk";

        private string PathFor(string executionId)
        {
            if (!SafeId.IsMatch(executionId)) throw new ArgumentException($"Unsafe execution id: '{executionId}'", nameof(executionId));
            return Path.Combine(_dir, $"{executionId}.json");
        }

        public Task SaveAsync(FlowStateRecord record, CancellationToken ct = default)
        {
            var path = PathFor(record.ExecutionId);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, FlowStateJson.Serialize(record));
            File.Move(tmp, path, overwrite: true); // atomic on NTFS/POSIX
            return Task.CompletedTask;
        }

        public Task<FlowStateRecord?> LoadAsync(string executionId, CancellationToken ct = default)
        {
            var path = PathFor(executionId);
            if (!File.Exists(path)) return Task.FromResult<FlowStateRecord?>(null);
            try
            {
                return Task.FromResult(FlowStateJson.Deserialize(File.ReadAllText(path)));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Corrupt flow-state file {Path}; treating as absent", path);
                return Task.FromResult<FlowStateRecord?>(null);
            }
        }

        public async Task<IReadOnlyList<FlowStateSummary>> ListAsync(CancellationToken ct = default)
        {
            var summaries = new List<FlowStateSummary>();
            foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
            {
                try
                {
                    var record = FlowStateJson.Deserialize(File.ReadAllText(file));
                    if (record?.Execution != null) summaries.Add(FlowStateJson.ToSummary(record));
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Skipping unreadable flow-state file {File}", file);
                }
            }
            summaries.Sort((a, b) => a.CheckpointedAt.CompareTo(b.CheckpointedAt));
            return summaries;
        }

        public Task DeleteAsync(string executionId, CancellationToken ct = default)
        {
            var path = PathFor(executionId);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // REDIS BACKEND
    // ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Key per execution (<c>stepflow:exec:{id}</c>) plus an index set for listing.
    /// Connects lazily on first use; a dead Redis degrades to save failures (logged by the caller),
    /// never to flow failure.
    /// </summary>
    public sealed class RedisFlowStateStore : IFlowStateStore
    {
        private const string IndexKey = "stepflow:exec:index";

        private readonly string _connection;
        private readonly ILogger? _logger;
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private ConnectionMultiplexer? _muxer;

        public RedisFlowStateStore(string connectionString, ILogger<RedisFlowStateStore>? logger = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required", nameof(connectionString));
            _connection = connectionString;
            _logger = logger;
        }

        public string ProviderName => "redis";

        private static RedisKey KeyFor(string executionId) => $"stepflow:exec:{executionId}";

        private async Task<IDatabase> DbAsync(CancellationToken ct)
        {
            if (_muxer is { IsConnected: true }) return _muxer.GetDatabase();
            await _connectLock.WaitAsync(ct);
            try
            {
                if (_muxer is not { IsConnected: true })
                {
                    _logger?.LogInformation("Connecting to Redis at {Connection}", _connection);
                    var muxer = await ConnectionMultiplexer.ConnectAsync(_connection);
                    _muxer = muxer;
                }
                return _muxer.GetDatabase();
            }
            finally
            {
                _connectLock.Release();
            }
        }

        public async Task SaveAsync(FlowStateRecord record, CancellationToken ct = default)
        {
            var db = await DbAsync(ct);
            // SET then SADD: a crash between the two leaves an index entry without a key,
            // which ListAsync prunes on its next pass. No data loss either way.
            await db.StringSetAsync(KeyFor(record.ExecutionId), FlowStateJson.Serialize(record));
            await db.SetAddAsync(IndexKey, record.ExecutionId);
        }

        public async Task<FlowStateRecord?> LoadAsync(string executionId, CancellationToken ct = default)
        {
            var db = await DbAsync(ct);
            var value = await db.StringGetAsync(KeyFor(executionId));
            return value.IsNull ? null : FlowStateJson.Deserialize(value.ToString());
        }

        public async Task<IReadOnlyList<FlowStateSummary>> ListAsync(CancellationToken ct = default)
        {
            var db = await DbAsync(ct);
            var ids = (await db.SetMembersAsync(IndexKey)).Select(v => v.ToString()).ToArray();
            if (ids.Length == 0) return Array.Empty<FlowStateSummary>();

            var values = await db.StringGetAsync(ids.Select(KeyFor).ToArray());
            var summaries = new List<FlowStateSummary>(values.Length);
            for (var i = 0; i < ids.Length; i++)
            {
                if (values[i].IsNull)
                {
                    // Index entry without a key (crash between SET and SADD, or manual DEL): prune.
                    await db.SetRemoveAsync(IndexKey, ids[i]);
                    continue;
                }
                var record = FlowStateJson.Deserialize(values[i].ToString());
                if (record?.Execution != null) summaries.Add(FlowStateJson.ToSummary(record));
            }
            summaries.Sort((a, b) => a.CheckpointedAt.CompareTo(b.CheckpointedAt));
            return summaries;
        }

        public async Task DeleteAsync(string executionId, CancellationToken ct = default)
        {
            var db = await DbAsync(ct);
            await db.KeyDeleteAsync(KeyFor(executionId));
            await db.SetRemoveAsync(IndexKey, executionId);
        }
    }
}
