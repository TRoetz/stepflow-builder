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
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.DataSource;

namespace StepFunctionsApp.DataExchange;

/// <summary>
/// Polls per-profile inbox folders (&lt;InboxDirectory&gt;/&lt;profileId&gt;/) for dropped customer files.
/// When a stable new file appears, executes the owning profile with { "filePath": ... } as input,
/// records an execution artifact, and moves the source file to processed/ (or failed/) so it is
/// never reprocessed. Mirrors HumanTaskCompletionMonitorService's polling pattern.
/// </summary>
public class DataExchangeFileMonitorService : BackgroundService
{
    private readonly DataExchangeProfileStore _profiles;
    private readonly DataExchangeExecutor _executor;
    private readonly DataExchangeExecutionLog _executions;
    private readonly DataExchangeOptions _options;
    private readonly ILogger<DataExchangeFileMonitorService> _logger;

    // Files currently being processed (guards against re-pickup while an execution is in flight).
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public DataExchangeFileMonitorService(
        DataExchangeProfileStore profiles,
        DataExchangeExecutor executor,
        DataExchangeExecutionLog executions,
        IOptions<DataExchangeOptions> options,
        ILogger<DataExchangeFileMonitorService> logger)
    {
        _profiles = profiles;
        _executor = executor;
        _executions = executions;
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
                _logger.LogWarning(ex, "DataExchange file monitor poll failed");
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
        var profiles = _profiles.LoadAll()
            .Where(p => p.IsActive && p.DataSource?.MediumType == DataSourceMediumType.File)
            .ToList();

        foreach (var profile in profiles)
        {
            ct.ThrowIfCancellationRequested();
            var inbox = Path.Combine(_options.InboxDirectory, DataExchangeProfileStore.ResolveId(profile));
            if (!Directory.Exists(inbox)) continue;

            // Stable files only: skip temp/partial writes and anything modified within the last 2 seconds.
            var cutoff = DateTime.UtcNow.AddSeconds(-2);
            foreach (var file in Directory.GetFiles(inbox)
                         .Where(f => !IsTransient(f) && new FileInfo(f).LastWriteTimeUtc <= cutoff))
            {
                if (!_inFlight.Add(file)) continue;
                try
                {
                    _logger.LogInformation("DataExchange inbox: processing {File} for profile '{Profile}'", file, ProfileName(profile));
                    var input = JObject.FromObject(new { filePath = file });
                    var result = await _executor.ExecuteAsync(DataExchangeProfileStore.ResolveId(profile), input, ct);
                    _executions.Record(result);
                    MoveTo(file, inbox, "processed");
                }
                catch (Exception ex)
                {
                    // Record a failure artifact so the UI monitor shows it; move to failed/ to avoid an infinite retry loop.
                    _logger.LogError(ex, "DataExchange inbox: execution failed for {File}", file);
                    var id = Guid.NewGuid().ToString("N")[..8];
                    _executions.Record(new JObject
                    {
                        ["success"] = false,
                        ["executionId"] = id,
                        ["profileId"] = DataExchangeProfileStore.ResolveId(profile),
                        ["sourceFile"] = file,
                        ["error"] = ex.Message
                    });
                    MoveTo(file, inbox, "failed");
                }
                finally
                {
                    _inFlight.Remove(file);
                }
            }
        }
    }

    private static bool IsTransient(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith('.')) return true; // hidden files
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".tmp" or ".part" or ".crdownload";
    }

    private void MoveTo(string file, string inbox, string subfolder)
    {
        try
        {
            var targetDir = Path.Combine(inbox, subfolder);
            Directory.CreateDirectory(targetDir);
            File.Move(file, Path.Combine(targetDir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Path.GetFileName(file)}"));
        }
        catch (Exception ex)
        {
            // A failed move is non-fatal: the file stays in place and will be retried on the next poll.
            _logger.LogWarning(ex, "DataExchange inbox: could not archive '{File}' to {Subfolder}/", file, subfolder);
        }
    }

    private static string ProfileName(DataExchangeProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DataExchangeProfileName) ? DataExchangeProfileStore.ResolveId(profile) : profile.DataExchangeProfileName;
}
