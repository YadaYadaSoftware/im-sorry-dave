namespace SorryDave.JiraSync.Core.Jira;

/// <summary>
/// The subset of a Jira issue this service tracks, parsed from either a REST response or a
/// webhook payload (both share the same <c>issue</c> shape).
/// </summary>
public record JiraIssueData
{
    public required string Key { get; init; }
    public required string ProjectKey { get; init; }
    public required string IssueType { get; init; }
    public required string Status { get; init; }
    public string? AssigneeAccountId { get; init; }
    public string? AssigneeDisplayName { get; init; }
    public string? ReporterAccountId { get; init; }
    public string? ReporterDisplayName { get; init; }
    public required string Summary { get; init; }
    public string? Description { get; init; }
    public List<string> Labels { get; init; } = new();
    /// <summary>AccountIds of users @mentioned in the description (for channel invites).</summary>
    public List<string> MentionedAccountIds { get; init; } = new();
    public required DateTimeOffset Updated { get; init; }
}
