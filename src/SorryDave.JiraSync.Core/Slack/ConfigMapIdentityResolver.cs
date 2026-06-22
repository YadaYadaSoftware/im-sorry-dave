using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;

namespace SorryDave.JiraSync.Core.Slack;

/// <summary>
/// Resolves a Jira user to a Slack user id via the static <c>Slack:UserMap</c> (Jira accountId or
/// displayName → Slack user id). Needs no enterprise identity API — works on any Jira/Slack plan.
/// </summary>
public sealed class ConfigMapIdentityResolver : IJiraSlackIdentityResolver
{
    private readonly SlackOptions _options;

    public ConfigMapIdentityResolver(IOptions<SlackOptions> options) => _options = options.Value;

    public Task<string?> ResolveSlackUserIdAsync(JiraUserRef user, CancellationToken ct = default)
    {
        string? id = null;
        if (!string.IsNullOrWhiteSpace(user.AccountId))
            _options.UserMap.TryGetValue(user.AccountId, out id);
        if (id is null && !string.IsNullOrWhiteSpace(user.DisplayName))
            _options.UserMap.TryGetValue(user.DisplayName, out id);
        return Task.FromResult(id);
    }
}
