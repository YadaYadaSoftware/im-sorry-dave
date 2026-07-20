namespace SorryDave.JiraSync.Core.Configuration;

/// <summary>
/// Slack integration configuration. Secrets follow the platform convention (user-secrets locally,
/// SSM <c>/jira-sync/Slack/*</c> in AWS). When <see cref="BotToken"/> is absent the integration is
/// dormant — provisioning commands report "Slack not configured" and event listeners no-op.
/// </summary>
public class SlackOptions
{
    public const string SectionName = "Slack";

    /// <summary>Bot user OAuth token (<c>xoxb-...</c>).</summary>
    public string? BotToken { get; set; }

    /// <summary>Signing secret for verifying inbound Slack requests (slash commands / events).</summary>
    public string? SigningSecret { get; set; }

    /// <summary>Issue types eligible for channel provisioning (e.g. <c>Idea</c>). Empty = all types.</summary>
    public List<string> EligibleIssueTypes { get; set; } = new();

    /// <summary>Statuses treated as terminal/closed — the linked channel is archived. Case-insensitive.</summary>
    public List<string> ClosedStatuses { get; set; } = new() { "Done", "Closed", "Resolved", "Archived", "Cancelled" };

    /// <summary>Slack user ids invited to every provisioned channel (e.g. team watchers), so the
    /// channel is visible to them without browsing.</summary>
    public List<string> InviteUserIds { get; set; } = new();

    /// <summary>Static Jira→Slack identity map: Jira <c>accountId</c> OR <c>displayName</c> → Slack
    /// user id. Used by <c>ConfigMapIdentityResolver</c> to invite the assignee/reporter without any
    /// enterprise identity API.</summary>
    public Dictionary<string, string> UserMap { get; set; } = new();

    /// <summary>Slash commands the app serves, by name without the leading slash (e.g. <c>post</c>).
    /// This is an allow-list and <b>absence means disabled</b> — a command plugin that exists in the
    /// container is not served unless it is named here, so a new command cannot ship enabled by
    /// oversight. Empty (the default) serves no slash commands at all.</summary>
    public List<string> EnabledCommands { get; set; } = new();

    /// <summary>Master switch for auto-inviting on provision and assignee change.</summary>
    public bool AutoInvite { get; set; } = true;

    /// <summary>How often the background sweep reconciles channel links against Slack (drift detection).</summary>
    public TimeSpan ReconciliationInterval { get; set; } = TimeSpan.FromMinutes(30);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);

    public bool IsEligible(string issueType)
        => EligibleIssueTypes.Count == 0
           || EligibleIssueTypes.Any(t => string.Equals(t, issueType, StringComparison.OrdinalIgnoreCase));

    public bool IsClosed(string status)
        => ClosedStatuses.Any(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a slash command is allowed to be registered. Leading slashes are tolerated so
    /// <c>/post</c> and <c>post</c> both work in configuration.</summary>
    public bool IsCommandEnabled(string commandName)
        => EnabledCommands.Any(c => string.Equals(c.TrimStart('/'), commandName.TrimStart('/'),
            StringComparison.OrdinalIgnoreCase));
}
