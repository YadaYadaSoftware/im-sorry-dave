using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;

namespace SorryDave.JiraSync.Core.WriteBack;

/// <summary>Periodically drains the write-back outbox. Each tick runs in its own DI scope.</summary>
public class WriteBackBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SyncOptions _options;
    private readonly ILogger<WriteBackBackgroundService> _logger;

    public WriteBackBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<SyncOptions> options,
        ILogger<WriteBackBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.OutboxPollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<WriteBackSender>();
                await sender.ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Write-back outbox processing failed; will retry next interval.");
            }
        }
    }
}
