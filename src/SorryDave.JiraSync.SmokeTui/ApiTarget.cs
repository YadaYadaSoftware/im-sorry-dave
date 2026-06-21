namespace SorryDave.JiraSync.SmokeTui;

/// <summary>
/// A configured API the console can talk to: a base URL plus, for secured backends, the webhook
/// shared secret the "simulate webhook" action must present. Bound from the <c>ApiTargets</c>
/// configuration section (each key is a target name).
/// </summary>
public sealed class ApiTarget
{
    public string BaseUrl { get; set; } = "";

    /// <summary>Shared secret for the secured <c>POST /webhooks/jira</c> endpoint, if the target
    /// requires one. Sourced from user-secrets/environment — never committed.</summary>
    public string? WebhookSecret { get; set; }
}

/// <summary>The resolved set of named targets and which one is active.</summary>
public sealed record ResolvedTargets(IReadOnlyDictionary<string, ApiTarget> Targets, string ActiveName)
{
    public ApiTarget Active => Targets[ActiveName];
}
