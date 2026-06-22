namespace SorryDave.JiraSync.Core.Slack;

/// <summary>What we know about a Jira user when trying to find their Slack identity. <see cref="Email"/>
/// is null on Jira Cloud (privacy); the Data-Center / admin-API resolvers populate it.</summary>
public sealed record JiraUserRef(string? AccountId, string? DisplayName, string? Email)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(AccountId)
                           && string.IsNullOrWhiteSpace(DisplayName)
                           && string.IsNullOrWhiteSpace(Email);
}

/// <summary>
/// Maps a Jira user to a Slack user id. Pluggable: multiple strategies are registered and tried in
/// order (config map now; Data-Center-email and Atlassian-admin-API as enterprise drop-ins). Returns
/// null when this strategy can't resolve the user — the caller falls through / skips.
/// </summary>
public interface IJiraSlackIdentityResolver
{
    Task<string?> ResolveSlackUserIdAsync(JiraUserRef user, CancellationToken ct = default);
}
