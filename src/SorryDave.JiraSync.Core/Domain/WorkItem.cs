namespace SorryDave.JiraSync.Core.Domain;

/// <summary>
/// Internal mirror of a Jira work item. Jira is authoritative for every field that is
/// also editable in Jira; this record reflects, never overrides, those fields.
/// </summary>
public class WorkItem
{
    /// <summary>The Jira issue key, e.g. "PROJ-123". Primary key.</summary>
    public string Key { get; set; } = default!;

    public string ProjectKey { get; set; } = default!;
    public string IssueType { get; set; } = default!;
    public string Status { get; set; } = default!;

    public string? AssigneeAccountId { get; set; }
    public string? AssigneeDisplayName { get; set; }
    public string? ReporterAccountId { get; set; }
    public string? ReporterDisplayName { get; set; }

    public string Summary { get; set; } = default!;
    public string? Description { get; set; }

    /// <summary>Labels stored via a value converter as a single column.</summary>
    public List<string> Labels { get; set; } = new();

    /// <summary>AccountIds @mentioned in the description (for channel invites). Stored like Labels.</summary>
    public List<string> MentionedAccountIds { get; set; } = new();

    /// <summary>
    /// The Jira <c>fields.updated</c> timestamp. Acts as the version marker used to
    /// discard stale or out-of-order events.
    /// </summary>
    public DateTimeOffset JiraUpdated { get; set; }

    /// <summary>Set when the issue no longer exists in Jira. Retained for audit.</summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSyncedUtc { get; set; }

    public List<ResourceMapping> Mappings { get; set; } = new();
}
