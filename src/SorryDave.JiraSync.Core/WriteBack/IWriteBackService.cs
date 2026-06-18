using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.WriteBack;

public interface IWriteBackService
{
    /// <summary>
    /// Queue a record for write-back to Jira. Idempotent on (WorkItemKey, RecordIdentity):
    /// a new identity is inserted; an unchanged resubmission is a no-op; a changed
    /// resubmission updates the content and re-queues an in-place edit.
    /// </summary>
    Task<WriteBackRecord> SubmitAsync(WriteBackSubmission submission, CancellationToken ct = default);
}
