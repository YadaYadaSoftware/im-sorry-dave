using SorryDave.JiraSync.Core.Jira;

namespace SorryDave.JiraSync.Core.Sync;

public enum SyncOutcome
{
    /// <summary>A new work item was created.</summary>
    Created,
    /// <summary>An existing work item was refreshed.</summary>
    Updated,
    /// <summary>The event was older than the stored version and was discarded.</summary>
    SkippedStale,
    /// <summary>The work item was marked deleted.</summary>
    Deleted
}

public interface IWorkItemSyncService
{
    /// <summary>Upsert an issue into the store, discarding stale/out-of-order data.</summary>
    Task<SyncOutcome> ApplyIssueAsync(JiraIssueData issue, CancellationToken ct = default);

    /// <summary>Mark a work item as deleted (soft delete, retained for audit).</summary>
    Task<SyncOutcome> ApplyDeletionAsync(string key, CancellationToken ct = default);
}
