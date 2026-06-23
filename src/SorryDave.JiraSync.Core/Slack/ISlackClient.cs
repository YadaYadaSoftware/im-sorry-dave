namespace SorryDave.JiraSync.Core.Slack;

/// <summary>A Slack channel as seen by the integration.</summary>
public sealed record SlackChannel(string Id, string Name, bool IsArchived);

/// <summary>
/// Thin wrapper over the Slack Web API used for channel provisioning, lifecycle, and context
/// reflection. Implemented by <c>SlackWebApiClient</c>; unit tests use a test double.
/// </summary>
public interface ISlackClient
{
    /// <summary>Create a public channel. Throws if the name is taken (callers handle collisions).</summary>
    Task<SlackChannel> CreateChannelAsync(string name, CancellationToken ct = default);

    /// <summary>Find a channel by exact name (including archived), or null.</summary>
    Task<SlackChannel?> FindChannelByNameAsync(string name, CancellationToken ct = default);

    /// <summary>Get a channel by id (for drift reconciliation), or null if it no longer exists.</summary>
    Task<SlackChannel?> GetChannelInfoAsync(string channelId, CancellationToken ct = default);

    /// <summary>Post a message; returns the message timestamp (ts) for pinning.</summary>
    Task<string> PostMessageAsync(string channelId, string text, CancellationToken ct = default);

    /// <summary>Post a Block Kit message (interactive cards); <paramref name="fallbackText"/> is the
    /// notification text. Returns the message ts.</summary>
    Task<string> PostBlocksAsync(string channelId, string fallbackText, object blocks, CancellationToken ct = default);

    Task SetTopicAsync(string channelId, string topic, CancellationToken ct = default);
    Task SetPurposeAsync(string channelId, string purpose, CancellationToken ct = default);
    Task ArchiveAsync(string channelId, CancellationToken ct = default);
    Task UnarchiveAsync(string channelId, CancellationToken ct = default);
    Task PinMessageAsync(string channelId, string messageTs, CancellationToken ct = default);

    /// <summary>Resolve a Slack user id by email, or null if no match (best-effort identity mapping).</summary>
    Task<string?> LookupUserIdByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Invite a user to a channel. No-op-safe if already a member. Returns true if the user
    /// was newly added, false if they were already in the channel.</summary>
    Task<bool> InviteAsync(string channelId, string userId, CancellationToken ct = default);
}
