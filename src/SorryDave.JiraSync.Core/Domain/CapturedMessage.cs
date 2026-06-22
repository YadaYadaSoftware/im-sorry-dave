namespace SorryDave.JiraSync.Core.Domain;

/// <summary>
/// A Slack message captured from a work-item channel, stored with thread fidelity so a conversation
/// window can be reconstructed for summarization. Keyed by (ChannelId, Ts) — redelivery is an upsert.
/// </summary>
public class CapturedMessage
{
    public long Id { get; set; }

    public string ChannelId { get; set; } = default!;
    public string WorkItemKey { get; set; } = default!;

    /// <summary>Slack message timestamp ("1700000000.000100") — unique within a channel and ordered.</summary>
    public string Ts { get; set; } = default!;

    /// <summary>Parent thread ts, or null for a top-level message.</summary>
    public string? ThreadTs { get; set; }

    public string? AuthorId { get; set; }
    public string Text { get; set; } = "";

    public bool IsDeleted { get; set; }

    public DateTimeOffset PostedUtc { get; set; }
    public DateTimeOffset CapturedUtc { get; set; }
}
