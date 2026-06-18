using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;

namespace SorryDave.JiraSync.Core.Sync;

/// <summary>
/// Drives reconciliation: an optional backfill on startup, then a periodic sweep. Each run
/// executes in its own DI scope because the runner depends on the scoped DbContext.
/// </summary>
public class ReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SyncOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReconciliationBackgroundService> _logger;

    public ReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<SyncOptions> options,
        TimeProvider clock,
        ILogger<ReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Last point we know we have reflected up to; sweeps query from here minus overlap.
        var highWater = _clock.GetUtcNow();

        if (_options.BackfillOnStartup)
            await RunSafely(r => r.BackfillAsync(stoppingToken), "backfill", stoppingToken);

        using var timer = new PeriodicTimer(_options.ReconciliationInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var since = highWater - _options.ReconciliationOverlap;
            var sweepStart = _clock.GetUtcNow();
            await RunSafely(r => r.SweepAsync(since, stoppingToken), "sweep", stoppingToken);
            highWater = sweepStart;
        }
    }

    private async Task RunSafely(Func<ReconciliationRunner, Task> work, string label, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<ReconciliationRunner>();
            await work(runner);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconciliation {Label} failed; will retry next interval.", label);
        }
    }
}
