using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // HUMAN TASKS — durable "wait for a human" state support
    // A HumanTask state suspends the execution, records what to show the human and how
    // completion arrives. Completion can come through the API (default) or an external
    // channel implemented by IHumanTaskCompletionProvider (e.g. a watched file).
    // ═══════════════════════════════════════════════════════════════════════════════

    public enum HumanTaskStatus
    {
        Pending,
        Completed
    }

    /// <summary>Thrown by the interpreter when a HumanTask state suspends an execution. Carries the resume point.</summary>
    [Serializable]
    public class ExecutionSuspendedException : Exception
    {
        public string StateName { get; }
        public string TaskId { get; }
        /// <summary>State to run when the task completes, or null if the human task is terminal (End).</summary>
        public string? NextState { get; }

        public ExecutionSuspendedException(string stateName, string taskId, string? nextState)
            : base($"Execution suspended at '{stateName}' awaiting human task {taskId}")
        {
            StateName = stateName;
            TaskId = taskId;
            NextState = nextState;
        }
    }

    /// <summary>Options for the human-task subsystem. Bound from the "HumanTasks" section of appsettings.json.</summary>
    public class HumanTaskOptions
    {
        public const string SectionName = "HumanTasks";

        /// <summary>Directory where pending/completed task records are persisted (relative paths resolve against the process working directory).</summary>
        public string DiskPath { get; set; } = "flow-state/human-tasks";

        /// <summary>How often the completion monitor polls for external completions, in seconds.</summary>
        public int PollIntervalSeconds { get; set; } = 5;

        /// <summary>Fallback directory for file-based completions when a state's Completion config omits "Directory".</summary>
        public string FileMonitorDefaultDirectory { get; set; } = "human-task-completions";
    }

    /// <summary>A human task awaiting external completion. Persisted by IHumanTaskStore and referenced from a suspended execution's checkpoint.</summary>
    public class HumanTaskRecord
    {
        public string TaskId { get; set; } = "";
        public string ExecutionId { get; set; } = "";
        public string StateMachineName { get; set; } = "";
        /// <summary>The state that created this task.</summary>
        public string StateName { get; set; } = "";
        /// <summary>State to run when the task completes, or null if the human task is terminal (End).</summary>
        public string? NextState { get; set; }
        /// <summary>True when the human task ends the flow. The completion result becomes the execution's output.</summary>
        public bool IsEnd { get; set; }
        /// <summary>The JSON path within the state input where the completion result is placed (null = the result replaces the entire input).</summary>
        public string? ResultPath { get; set; }

        // ── What to show the human ────────────────────────────────
        public string? Title { get; set; }
        public string? Assignee { get; set; }
        /// <summary>The state's "Task" config plus a snapshot of the flow input at suspension time.</summary>
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken? Payload { get; set; }

        // ── How completion arrives ────────────────────────────────
        /// <summary>Completion provider key from the state's Completion.Type ("api", "file", ...).</summary>
        public string CompletionType { get; set; } = "api";
        /// <summary>The full "Completion" object (provider-specific config).</summary>
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken? CompletionConfig { get; set; }

        // ── Lifecycle ─────────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public HumanTaskStatus Status { get; set; } = HumanTaskStatus.Pending;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        /// <summary>The human's result, stored when the task completes.</summary>
        [JsonConverter(typeof(NullableJTokenConverter))]
        public JToken? Result { get; set; }
    }

    public interface IHumanTaskStore
    {
        string ProviderName { get; }
        Task SaveAsync(HumanTaskRecord record, CancellationToken ct = default);
        Task<HumanTaskRecord?> LoadAsync(string taskId, CancellationToken ct = default);
        /// <summary>All tasks (any status), oldest first.</summary>
        Task<IReadOnlyList<HumanTaskRecord>> ListAsync(CancellationToken ct = default);
    }

    /// <summary>Disk-backed human task store — one JSON file per task under the configured directory. Mirrors DiskFlowStateStore's atomic-write pattern.</summary>
    public class DiskHumanTaskStore : IHumanTaskStore
    {
        private static readonly Regex SafeIdRegex = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

        private readonly string _directory;
        private readonly ILogger<DiskHumanTaskStore>? _logger;

        public DiskHumanTaskStore(string directory, ILogger<DiskHumanTaskStore>? logger = null)
        {
            _directory = Path.GetFullPath(directory);
            _logger = logger;
            Directory.CreateDirectory(_directory);
        }

        public string ProviderName => "disk";

        private static void ValidateId(string taskId)
        {
            if (!SafeIdRegex.IsMatch(taskId)) throw new ArgumentException($"Invalid human task id: '{taskId}'");
        }

        private string PathFor(string taskId) => Path.Combine(_directory, $"{taskId}.json");

        public Task SaveAsync(HumanTaskRecord record, CancellationToken ct = default)
        {
            ValidateId(record.TaskId);
            var path = PathFor(record.TaskId);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(record, Formatting.Indented));
            File.Move(temp, path, overwrite: true);
            return Task.CompletedTask;
        }

        public async Task<HumanTaskRecord?> LoadAsync(string taskId, CancellationToken ct = default)
        {
            ValidateId(taskId);
            var path = PathFor(taskId);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonConvert.DeserializeObject<HumanTaskRecord>(await File.ReadAllTextAsync(path, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning("Failed to read human task {TaskId}: {Message}", taskId, ex.Message);
                return null;
            }
        }

        public async Task<IReadOnlyList<HumanTaskRecord>> ListAsync(CancellationToken ct = default)
        {
            var tasks = new List<HumanTaskRecord>();
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                try
                {
                    var record = JsonConvert.DeserializeObject<HumanTaskRecord>(await File.ReadAllTextAsync(file, ct));
                    if (record != null) tasks.Add(record);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogWarning("Skipping unreadable human task file {File}: {Message}", file, ex.Message);
                }
            }
            return tasks.OrderBy(t => t.CreatedAtUtc).ToList();
        }
    }

    /// <summary>Shared dotted-path setter used by both the interpreter and human-task completion. Creates intermediate objects.</summary>
    internal static class JsonPaths
    {
        public static void Set(JToken root, string path, JToken value)
        {
            if (path == "$" || string.IsNullOrEmpty(path)) return; // caller handles whole-input replacement

            var segments = path.StartsWith("$.") ? path[2..].Split('.') : path.StartsWith("$") ? path[1..].Split('.') : path.Split('.');
            JToken current = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                var seg = segments[i];
                if (current is JObject obj)
                {
                    if (!obj.TryGetValue(seg, out var next))
                    {
                        next = new JObject();
                        obj[seg] = next;
                    }
                    current = next;
                }
            }

            if (current is JObject finalObj)
                finalObj[segments[^1]] = value;
        }
    }
}
