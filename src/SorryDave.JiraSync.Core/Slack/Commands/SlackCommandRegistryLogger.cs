using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SorryDave.JiraSync.Core.Slack.Commands;

/// <summary>
/// Reports the served command surface once at startup. Because the allow-list fails closed, a command
/// missing from production looks identical to a command that was never built — this makes the
/// difference visible without attaching a debugger.
/// </summary>
public sealed class SlackCommandRegistryLogger : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlackCommandRegistryLogger> _logger;

    public SlackCommandRegistryLogger(IServiceScopeFactory scopeFactory, ILogger<SlackCommandRegistryLogger> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ISlackCommandRegistry>();

        var registered = registry.RegisteredCommands.Select(p => "/" + p.Name).ToList();
        var skipped = registry.SkippedCommands.Select(n => "/" + n).ToList();

        if (registered.Count == 0)
            _logger.LogWarning(
                "No Slack slash commands are registered. Commands are opt-in via Slack:EnabledCommands; " +
                "{SkippedCount} plugin(s) were skipped: {Skipped}",
                skipped.Count, skipped.Count == 0 ? "(none found)" : string.Join(", ", skipped));
        else
            _logger.LogInformation("Slack slash commands registered: {Registered}", string.Join(", ", registered));

        if (registered.Count > 0 && skipped.Count > 0)
            _logger.LogInformation(
                "Slack slash commands skipped (absent from Slack:EnabledCommands): {Skipped}",
                string.Join(", ", skipped));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
