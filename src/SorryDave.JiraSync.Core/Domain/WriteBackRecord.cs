namespace SorryDave.JiraSync.Core.Domain;

public enum WriteBackKind
{
    Decision = 1,
    Answer = 2,
    Summary = 3
}

public enum WriteBackStatus
{
    /// <summary>Queued, awaiting delivery to Jira.</summary>
    Pending = 1,
    /// <summary>Successfully written to Jira.</summary>
    Sent = 2,
    /// <summary>Transient failure; will be retried after <see cref="WriteBackRecord.NextAttemptUtc"/>.</summary>
    Retrying = 3,
    /// <summary>Permanent failure; surfaced to operators, not retried automatically.</summary>
    Failed = 4
}

/// <summary>
/// Outbox row for a single managed decision/answer/summary destined for Jira. Identity is
/// (<see cref="WorkItemKey"/>, <see cref="RecordIdentity"/>); resubmitting the same identity
/// edits in place rather than creating a duplicate.
/// </summary>
public class WriteBackRecord
{
    public Guid Id { get; set; }

    public string WorkItemKey { get; set; } = default!;

    /// <summary>Caller-supplied stable identity for this logical record (idempotency key).</summary>
    public string RecordIdentity { get; set; } = default!;

    public WriteBackKind Kind { get; set; }
    public string Content { get; set; } = default!;

    /// <summary>Link back to the originating conversation (e.g. a Slack thread permalink).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>The responsible person the record is attributed to.</summary>
    public string? Author { get; set; }

    public WriteBackStatus Status { get; set; }

    /// <summary>The Jira comment id once written, enabling idempotent in-place edits.</summary>
    public string? JiraCommentId { get; set; }

    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>Earliest time the sender may (re)attempt delivery.</summary>
    public DateTimeOffset NextAttemptUtc { get; set; }
}
