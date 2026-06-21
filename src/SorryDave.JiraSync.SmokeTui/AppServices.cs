using Microsoft.Extensions.Configuration;
using SorryDave.JiraSync.SmokeTui.Api;

namespace SorryDave.JiraSync.SmokeTui;

/// <summary>
/// Resolves the set of API targets the console can drive and which one is active. Targets come from
/// the <c>ApiTargets</c> config section (with secrets from user-secrets). When launched by the
/// AppHost, Aspire's injected endpoint (<c>services:api:http:0</c>) is folded into the <c>local</c>
/// target so the menu stays local-vs-aws. Active selection is <c>--target</c> &gt;
/// <c>ActiveApiTarget</c> &gt; <c>local</c>.
/// </summary>
public static class AppServices
{
    public static ResolvedTargets Build(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(typeof(AppServices).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        return Resolve(config);
    }

    /// <summary>Pure resolution from configuration — unit-testable with an in-memory config.</summary>
    public static ResolvedTargets Resolve(IConfiguration config)
    {
        var targets = new Dictionary<string, ApiTarget>(StringComparer.OrdinalIgnoreCase);
        config.GetSection("ApiTargets").Bind(targets);
        targets = targets
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.BaseUrl))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        // Launched by the AppHost: Aspire injects the API's endpoint (a dynamic port). Fold it into
        // the "local" target so the menu stays a simple local-vs-aws choice and "local" reaches the
        // running AppHost API — start the AppHost, then just pick local or aws in the TUI.
        var aspireUrl = config["services:api:http:0"] ?? config["services:api:https:0"];
        if (!string.IsNullOrWhiteSpace(aspireUrl))
        {
            if (targets.TryGetValue("local", out var local))
                local.BaseUrl = aspireUrl;
            else
                targets["local"] = new ApiTarget { BaseUrl = aspireUrl };
        }

        // Never leave the console without somewhere to point.
        if (targets.Count == 0)
            targets["local"] = new ApiTarget { BaseUrl = "http://localhost:5050" };

        // --target wins (truly explicit per run); then the configured default; then local.
        string active =
            Find(targets, config["target"]) ??
            Find(targets, config["ActiveApiTarget"]) ??
            (targets.ContainsKey("local") ? "local" : null) ??
            targets.Keys.First();

        return new ResolvedTargets(targets, active);
    }

    /// <summary>Build the API client for a target, applying its webhook secret to secured calls.</summary>
    public static IApiClient CreateClient(ApiTarget target)
    {
        var http = new HttpClient { BaseAddress = new Uri(target.BaseUrl.TrimEnd('/') + "/") };
        return new ApiClient(http, target.WebhookSecret);
    }

    private static string? Find(IReadOnlyDictionary<string, ApiTarget> targets, string? name)
        => !string.IsNullOrWhiteSpace(name) && targets.ContainsKey(name)
            ? targets.Keys.First(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
            : null;
}
