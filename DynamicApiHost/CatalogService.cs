using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using StepFlow.DynamicApi;

namespace StepFlow.DynamicApi.Host;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// CATALOG SERVICE - in-memory snapshot of the backend's published dynamic APIs.
// Pulls GET api/dynamic/apis?published=true from the engine on startup and then
// every Sync:IntervalSeconds (default 30 s). A failed pull never clears a good
// snapshot; IsLoaded flips true only after the first successful pull, until which
// the dispatcher answers 503.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

/// <summary>Published-API catalog synced from the backend engine.</summary>
public sealed class CatalogService : IHostedService
{
    private static readonly JsonSerializer WireJson = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SyncOptions _sync;
    private readonly ILogger<CatalogService> _logger;

    private volatile IReadOnlyList<DynamicApiDefinition> _apis = Array.Empty<DynamicApiDefinition>();
    private string? _lastHash;

    public CatalogService(IHttpClientFactory httpClientFactory, IOptions<SyncOptions> sync, ILogger<CatalogService>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _sync = sync.Value;
        _logger = logger ?? NullLogger<CatalogService>.Instance;
    }

    /// <summary>Current snapshot (empty until the first successful pull).</summary>
    public IReadOnlyList<DynamicApiDefinition> Apis => _apis;

    /// <summary>True after the first successful pull.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>UTC time of the last successful pull.</summary>
    public DateTime? LastSyncUtc { get; private set; }

    /// <summary>Pulls the published-API list once. Failures keep the last good snapshot.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient(EngineOptions.ClientName);
            using var response = await client.GetAsync("api/dynamic/apis?published=true");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Catalog pull failed: engine returned {StatusCode} for the published-API list", (int)response.StatusCode);
                return;
            }

            var raw = await response.Content.ReadAsStringAsync();
            IsLoaded = true;
            LastSyncUtc = DateTime.UtcNow;

            // Only swap the snapshot when the payload actually changed.
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            if (hash == _lastHash) return;

            var apis = WireJson.Deserialize<List<DynamicApiDefinition>>(new JsonTextReader(new StringReader(raw))) ?? new List<DynamicApiDefinition>();
            _apis = apis;
            _lastHash = hash;
            _logger.LogInformation("Catalog synced: {Count} published dynamic API(s)", apis.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Catalog pull failed");
        }
    }

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Hosted-service contract: StartAsync must return promptly - the generic host awaits it before
        // starting Kestrel, so the poll loop runs as a background task and is drained in StopAsync.
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_loopTask != null) await _loopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync().ConfigureAwait(false); // first pull; failures are logged and retried on the next tick
        var interval = TimeSpan.FromSeconds(Math.Max(5, _sync.IntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // host shutdown - stop polling.
        }
    }
}
