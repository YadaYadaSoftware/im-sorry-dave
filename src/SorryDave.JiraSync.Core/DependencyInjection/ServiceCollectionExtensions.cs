using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.Mapping;
using SorryDave.JiraSync.Core.Persistence;
using SorryDave.JiraSync.Core.Slack;
using SorryDave.JiraSync.Core.Sync;
using SorryDave.JiraSync.Core.WriteBack;

namespace SorryDave.JiraSync.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the jira-sync-core services: persistence, the Jira client (real when
    /// credentials are configured, otherwise an in-memory fake), sync/mapping/write-back
    /// services, and the reconciliation + outbox background workers.
    /// </summary>
    public static IServiceCollection AddJiraSyncCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JiraOptions>(configuration.GetSection(JiraOptions.SectionName));
        services.Configure<WebhookOptions>(configuration.GetSection(WebhookOptions.SectionName));
        services.Configure<SyncOptions>(configuration.GetSection(SyncOptions.SectionName));
        services.Configure<SlackOptions>(configuration.GetSection(SlackOptions.SectionName));

        services.TryAddSingletonTimeProvider();

        var connectionString = configuration.GetConnectionString("JiraSync") ?? "Data Source=jirasync.db";
        services.AddDbContext<JiraSyncDbContext>(o => o.UseSqlite(connectionString));

        var jira = configuration.GetSection(JiraOptions.SectionName).Get<JiraOptions>() ?? new JiraOptions();
        var useFake = jira.UseFake ?? !jira.HasCredentials;
        if (useFake)
            services.AddSingletonFakeJira();
        else
            services.AddRealJira(jira);

        // Slack channel provisioning. Always registered so the API's /slack endpoints resolve; the
        // service short-circuits (and the change listener no-ops) when no bot token is configured.
        // The same instance serves as the explicit-command service and the Jira-event listener.
        var slack = configuration.GetSection(SlackOptions.SectionName).Get<SlackOptions>() ?? new SlackOptions();
        services.AddHttpClient<ISlackClient, SlackWebApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://slack.com/api/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", slack.BotToken ?? "unconfigured");
        });
        services.AddScoped<SlackChannelService>();
        services.AddScoped<ISlackChannelService>(sp => sp.GetRequiredService<SlackChannelService>());
        services.AddScoped<IWorkItemChangeListener>(sp => sp.GetRequiredService<SlackChannelService>());
        // Jira→Slack identity resolvers (tried in order). Config-map ships now; Data-Center-email and
        // Atlassian-admin-API resolvers drop into this chain later.
        services.AddScoped<IJiraSlackIdentityResolver, ConfigMapIdentityResolver>();

        services.AddScoped<IWorkItemSyncService, WorkItemSyncService>();
        services.AddScoped<IMappingStore, MappingStore>();
        services.AddScoped<IWriteBackService, WriteBackService>();
        services.AddScoped<WebhookProcessor>();
        services.AddScoped<ReconciliationRunner>();
        services.AddScoped<WriteBackSender>();

        services.AddHostedService<ReconciliationBackgroundService>();
        services.AddHostedService<WriteBackBackgroundService>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);
    }

    private static void AddSingletonFakeJira(this IServiceCollection services)
    {
        services.AddSingleton<FakeJiraClient>();
        services.AddSingleton<IJiraClient>(sp => sp.GetRequiredService<FakeJiraClient>());
    }

    private static void AddRealJira(this IServiceCollection services, JiraOptions jira)
    {
        services.AddTransient<JiraRateLimitHandler>();

        var authHeader = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{jira.Email}:{jira.ApiToken}")));

        services.AddHttpClient<IJiraClient, JiraRestClient>(client =>
            {
                client.BaseAddress = new Uri(jira.BaseUrl!.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Authorization = authHeader;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddHttpMessageHandler<JiraRateLimitHandler>();
    }
}
