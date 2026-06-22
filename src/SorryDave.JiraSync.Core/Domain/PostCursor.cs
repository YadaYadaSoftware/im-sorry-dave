namespace SorryDave.JiraSync.Core.Domain;

/// <summary>
/// Per-channel marker of the last successful <c>/post</c>. The next <c>/post</c> summarizes messages
/// posted after <see cref="LastPostedTs"/>. Advances only when a <c>/post</c> writes back successfully,
/// so a no-op/rejected/failed post retries the same window.
/// </summary>
public class PostCursor
{
    /// <summary>Slack channel id — primary key (one cursor per channel).</summary>
    public string ChannelId { get; set; } = default!;

    public string WorkItemKey { get; set; } = default!;

    /// <summary>Slack ts up to which the last successful <c>/post</c> covered; null = never posted.</summary>
    public string? LastPostedTs { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
