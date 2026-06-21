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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);

    public bool IsEligible(string issueType)
        => EligibleIssueTypes.Count == 0
           || EligibleIssueTypes.Any(t => string.Equals(t, issueType, StringComparison.OrdinalIgnoreCase));

    public bool IsClosed(string status)
        => ClosedStatuses.Any(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
}
