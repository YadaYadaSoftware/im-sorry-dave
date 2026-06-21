namespace SorryDave.JiraSync.Core.Sync;

/// <summary>
/// A status/assignee change applied to a mirrored work item. Emitted by
/// <see cref="WorkItemSyncService"/> so downstream capabilities (e.g. Slack channel lifecycle and
/// context reflection) can react to Jira changes without polling.
/// </summary>
public sealed record WorkItemChange
{
    public required string Key { get; init; }
    public required string Status { get; init; }
    public string? PreviousStatus { get; init; }
    public string? AssigneeAccountId { get; init; }
    public string? AssigneeDisplayName { get; init; }
    public string? PreviousAssigneeAccountId { get; init; }

    public bool StatusChanged => !string.Equals(Status, PreviousStatus, StringComparison.Ordinal);
    public bool AssigneeChanged => !string.Equals(AssigneeAccountId, PreviousAssigneeAccountId, StringComparison.Ordinal);
}

/// <summary>
/// Notified when a mirrored work item's status or assignee changes. Implementations MUST be
/// best-effort — the sync path swallows and logs listener failures so a downstream outage cannot
/// block Jira mirroring. No listeners are registered when their feature is unconfigured.
/// </summary>
public interface IWorkItemChangeListener
{
    Task OnWorkItemChangedAsync(WorkItemChange change, CancellationToken ct = default);
}
