using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SorryDave.JiraSync.Core.DependencyInjection;
using SorryDave.JiraSync.Core.Jira;

namespace SorryDave.JiraSync.SmokeTui;

/// <summary>
/// Builds the service provider the TUI drives. It reuses the same <c>AddJiraSyncCore</c>
/// registration as the API so behavior is identical, but it never starts the hosted
/// background workers — the TUI invokes the runners (reconciliation, outbox) on demand.
/// </summary>
public static class AppServices
{
    public static IServiceProvider Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Console logging would corrupt the Terminal.Gui display; silence it.
        builder.Logging.ClearProviders();

        // Default to a smoke-test-specific SQLite file unless overridden.
        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("JiraSync")))
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:JiraSync"] = "Data Source=smoketui.db"
            });
        }

        builder.Services.AddJiraSyncCore(builder.Configuration);

        // Build the host but DO NOT start it, so hosted background services stay dormant.
        return builder.Build().Services;
    }

    /// <summary>True when the in-memory fake Jira client is active (no real credentials).</summary>
    public static bool IsFakeMode(IServiceProvider provider)
        => provider.GetRequiredService<IJiraClient>() is FakeJiraClient;
}
