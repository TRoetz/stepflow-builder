using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // HUMAN TASK COMPLETION PROVIDERS — channels through which a suspended human task
    // can be completed from outside the flow. Selected per state via:
    //   "Completion": { "Type": "api" | "file", ...provider-specific config }
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>A channel through which a suspended human task can be completed externally.</summary>
    public interface IHumanTaskCompletionProvider
    {
        /// <summary>Registry key used by a state's Completion.Type.</summary>
        string Type { get; }

        /// <summary>Poll for external completion of the task. Return the result payload, or null when not complete yet.</summary>
        Task<JToken?> CheckForCompletionAsync(HumanTaskRecord task, CancellationToken ct);

        /// <summary>Cleanup hook after a task completes (e.g. consume the completion file). Must tolerate missing artifacts.</summary>
        Task OnCompletedAsync(HumanTaskRecord task, JToken result, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Default completion channel: the human calls POST /api/human-tasks/{id}/complete. Nothing to poll.</summary>
    public sealed class ApiCompletionProvider : IHumanTaskCompletionProvider
    {
        public string Type => "api";

        public Task<JToken?> CheckForCompletionAsync(HumanTaskRecord task, CancellationToken ct) =>
            Task.FromResult<JToken?>(null);
    }

    /// <summary>Watches a directory for a per-task completion file (default "{taskId}.json"); its JSON content is the human's result.</summary>
    public sealed class FileMonitorCompletionProvider : IHumanTaskCompletionProvider
    {
        private readonly HumanTaskOptions _options;
        private readonly ILogger<FileMonitorCompletionProvider>? _logger;

        public FileMonitorCompletionProvider(IOptions<HumanTaskOptions> options, ILogger<FileMonitorCompletionProvider>? logger = null)
        {
            _options = options.Value;
            _logger = logger;
        }

        public string Type => "file";

        private static string ResolvePath(HumanTaskRecord task, HumanTaskOptions options)
        {
            var directory = task.CompletionConfig?["Directory"]?.ToString() ?? options.FileMonitorDefaultDirectory;
            var fileName = (task.CompletionConfig?["FileName"]?.ToString() ?? "{taskId}.json").Replace("{taskId}", task.TaskId);
            return Path.Combine(Path.GetFullPath(directory), fileName);
        }

        public Task<JToken?> CheckForCompletionAsync(HumanTaskRecord task, CancellationToken ct)
        {
            var path = ResolvePath(task, _options);
            if (!File.Exists(path)) return Task.FromResult<JToken?>(null);
            try
            {
                return Task.FromResult<JToken?>(JToken.Parse(File.ReadAllText(path)));
            }
            catch (Exception ex)
            {
                // Partial write or invalid JSON: leave it in place and retry on the next poll.
                _logger?.LogWarning("Completion file for human task {TaskId} is not valid JSON yet: {Message}", task.TaskId, ex.Message);
                return Task.FromResult<JToken?>(null);
            }
        }

        public Task OnCompletedAsync(HumanTaskRecord task, JToken result, CancellationToken ct)
        {
            var path = ResolvePath(task, _options);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }
    }

    /// <summary>Background monitor that polls non-API completion providers and completes pending human tasks.</summary>
    public sealed class HumanTaskCompletionMonitorService : BackgroundService
    {
        private readonly IHumanTaskStore _store;
        private readonly StepFunctionService _stepService;
        private readonly IReadOnlyDictionary<string, IHumanTaskCompletionProvider> _providers;
        private readonly HumanTaskOptions _options;
        private readonly ILogger<HumanTaskCompletionMonitorService> _logger;

        public HumanTaskCompletionMonitorService(
            IHumanTaskStore store,
            StepFunctionService stepService,
            IEnumerable<IHumanTaskCompletionProvider> providers,
            IOptions<HumanTaskOptions> options,
            ILogger<HumanTaskCompletionMonitorService> logger)
        {
            _store = store;
            _stepService = stepService;
            _providers = providers.ToDictionary(p => p.Type);
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Human task completion poll failed");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task PollOnceAsync(CancellationToken ct)
        {
            var pending = (await _store.ListAsync(ct)).Where(t => t.Status == HumanTaskStatus.Pending).ToList();
            foreach (var task in pending)
            {
                ct.ThrowIfCancellationRequested();
                if (!_providers.TryGetValue(task.CompletionType, out var provider)) continue; // "api" or unknown: completion arrives via the API only

                JToken? result;
                try
                {
                    result = await provider.CheckForCompletionAsync(task, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Completion check failed for human task {TaskId} ({Type})", task.TaskId, task.CompletionType);
                    continue;
                }

                if (result == null) continue;

                try
                {
                    await _stepService.CompleteHumanTaskAsync(task.TaskId, result, ct);
                }
                catch (Exception ex)
                {
                    // Leave the completion artifact in place so the next poll retries.
                    _logger.LogError(ex, "Failed to complete human task {TaskId}; will retry on next poll", task.TaskId);
                    continue;
                }

                try
                {
                    await provider.OnCompletedAsync(task, result, ct);
                }
                catch (Exception ex)
                {
                    // The task is already completed; a failed cleanup must not block the remaining tasks.
                    _logger.LogError(ex, "Failed to consume completion artifact for human task {TaskId}", task.TaskId);
                }
            }
        }
    }
}
