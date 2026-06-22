namespace SorryDave.JiraSync.Core.Domain;

public enum CandidateStatus
{
    Pending = 1,
    Confirmed = 2,
    Rejected = 3,
}

/// <summary>
/// A decision/answer/summary candidate extracted by Claude from a conversation window, awaiting human
/// confirmation before it is written back to Jira.
/// </summary>
public class SummaryCandidate
{
    public Guid Id { get; set; }

    public string ChannelId { get; set; } = default!;
    public string WorkItemKey { get; set; } = default!;

    public WriteBackKind Kind { get; set; }
    public string Content { get; set; } = default!;

    /// <summary>Grounding: a short quote/reference to the message(s) the candidate is based on.</summary>
    public string? Evidence { get; set; }
    public double Confidence { get; set; }

    /// <summary>Stable idempotency key carried into write-back so re-confirming never duplicates.</summary>
    public string RecordIdentity { get; set; } = default!;

    public CandidateStatus Status { get; set; } = CandidateStatus.Pending;

    /// <summary>The conversation window this candidate came from (Slack ts bounds).</summary>
    public string? WindowFromTs { get; set; }
    public string? WindowToTs { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}
