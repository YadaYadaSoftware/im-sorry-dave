using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;

namespace SorryDave.JiraSync.Core.Slack;

/// <summary>
/// Periodically reconciles Slack channel links against Slack's state (dangling links + archived-state
/// drift). Each run executes in its own DI scope (the service depends on the scoped DbContext). No-op
/// when Slack is unconfigured.
/// </summary>
public class SlackReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SlackOptions _options;
    private readonly ILogger<SlackReconciliationBackgroundService> _logger;

    public SlackReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<SlackOptions> options,
        ILogger<SlackReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured) return; // nothing to reconcile

        using var timer = new PeriodicTimer(_options.ReconciliationInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ISlackChannelService>();
                await service.ReconcileLinksAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Slack reconciliation failed; will retry next interval.");
            }
        }
    }
}
