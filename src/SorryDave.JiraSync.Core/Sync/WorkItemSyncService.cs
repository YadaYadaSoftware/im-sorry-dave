using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.Persistence;

namespace SorryDave.JiraSync.Core.Sync;

/// <summary>
/// Applies Jira issue data to the internal store. Jira is authoritative: the stored
/// <see cref="WorkItem.JiraUpdated"/> acts as a version marker so events that are older than
/// what we already have are discarded (handles dropped/out-of-order webhook delivery).
/// </summary>
public class WorkItemSyncService : IWorkItemSyncService
{
    private readonly JiraSyncDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<WorkItemSyncService> _logger;

    public WorkItemSyncService(JiraSyncDbContext db, TimeProvider clock, ILogger<WorkItemSyncService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SyncOutcome> ApplyIssueAsync(JiraIssueData issue, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var existing = await _db.WorkItems.FirstOrDefaultAsync(w => w.Key == issue.Key, ct);

        if (existing is null)
        {
            _db.WorkItems.Add(new WorkItem
            {
                Key = issue.Key,
                ProjectKey = issue.ProjectKey,
                IssueType = issue.IssueType,
                Status = issue.Status,
                AssigneeAccountId = issue.AssigneeAccountId,
                AssigneeDisplayName = issue.AssigneeDisplayName,
                ReporterDisplayName = issue.ReporterDisplayName,
                Summary = issue.Summary,
                Description = issue.Description,
                Labels = issue.Labels,
                JiraUpdated = issue.Updated,
                FirstSeenUtc = now,
                LastSyncedUtc = now
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Mirrored new work item {Key}.", issue.Key);
            return SyncOutcome.Created;
        }

        // Stale guard: discard strictly-older events. Equal timestamps are applied (idempotent).
        if (issue.Updated < existing.JiraUpdated)
        {
            _logger.LogDebug(
                "Discarding stale event for {Key}: event {EventTs} < stored {StoredTs}.",
                issue.Key, issue.Updated, existing.JiraUpdated);
            return SyncOutcome.SkippedStale;
        }

        existing.ProjectKey = issue.ProjectKey;
        existing.IssueType = issue.IssueType;
        existing.Status = issue.Status;
        existing.AssigneeAccountId = issue.AssigneeAccountId;
        existing.AssigneeDisplayName = issue.AssigneeDisplayName;
        existing.ReporterDisplayName = issue.ReporterDisplayName;
        existing.Summary = issue.Summary;
        existing.Description = issue.Description;
        existing.Labels = issue.Labels;
        existing.JiraUpdated = issue.Updated;
        existing.LastSyncedUtc = now;
        existing.IsDeleted = false; // a fresh update means it exists again

        await _db.SaveChangesAsync(ct);
        return SyncOutcome.Updated;
    }

    public async Task<SyncOutcome> ApplyDeletionAsync(string key, CancellationToken ct = default)
    {
        var existing = await _db.WorkItems.FirstOrDefaultAsync(w => w.Key == key, ct);
        if (existing is null) return SyncOutcome.SkippedStale;

        existing.IsDeleted = true;
        existing.LastSyncedUtc = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Marked work item {Key} as deleted.", key);
        return SyncOutcome.Deleted;
    }
}
