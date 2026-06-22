using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.Summarization;

/// <summary>A message event captured from Slack (created/edited/deleted).</summary>
public sealed record IncomingMessage(
    string ChannelId, string Ts, string? ThreadTs, string? AuthorId, string Text,
    bool Deleted = false);

/// <summary>Outcome of a <c>/post</c>: the candidates extracted over the since-last-post window.</summary>
public sealed record PostResult(string Outcome, IReadOnlyList<SummaryCandidate> Candidates, string? Detail = null);

/// <summary>
/// Captures Slack conversation and runs the <c>/post</c> summarize → confirm → write-back loop.
/// Channels not linked to a work item are ignored.
/// </summary>
public interface IConversationSummarizer
{
    /// <summary>Capture (upsert) a message event for a linked channel; no-op for unlinked channels.</summary>
    Task CaptureAsync(IncomingMessage message, CancellationToken ct = default);

    /// <summary>Run <c>/post</c>: extract candidates over the messages since the last successful post.</summary>
    Task<PostResult> PostAsync(string channelId, CancellationToken ct = default);

    /// <summary>Smoke path (no Slack): extract candidates from operator-provided lines for a work item
    /// and persist them, so the extractor (real Claude when keyed) can be exercised from the TUI.</summary>
    Task<PostResult> SmokeSummarizeAsync(string workItemKey, IReadOnlyList<TranscriptLine> lines, CancellationToken ct = default);

    /// <summary>Confirm a candidate → Jira write-back; advances the channel cursor on success.</summary>
    Task<string> ConfirmAsync(Guid candidateId, string? confirmingUser, CancellationToken ct = default);

    /// <summary>Reject a candidate (no write-back; cursor unchanged).</summary>
    Task<string> RejectAsync(Guid candidateId, CancellationToken ct = default);
}
