using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Mapping;
using SorryDave.JiraSync.Core.Persistence;
using SorryDave.JiraSync.Core.Sync;

namespace SorryDave.JiraSync.Core.Slack;

/// <summary>
/// Provisions a Slack channel per work item (lazily, on explicit request), keeps the channel ↔
/// work-item link in the mapping store, and reflects Jira status/assignee changes into the channel
/// (archive on close, unarchive on reopen, topic + status messages). Only registered when Slack is
/// configured; all Jira-event reactions are no-ops when the item has no linked channel.
/// </summary>
public sealed class SlackChannelService : ISlackChannelService
{
    private readonly ISlackClient _slack;
    private readonly IMappingStore _mappings;
    private readonly JiraSyncDbContext _db;
    private readonly SlackOptions _options;
    private readonly JiraOptions _jira;
    private readonly ILogger<SlackChannelService> _logger;
    private readonly IReadOnlyList<IJiraSlackIdentityResolver> _resolvers;

    public SlackChannelService(
        ISlackClient slack,
        IMappingStore mappings,
        JiraSyncDbContext db,
        IOptions<SlackOptions> options,
        IOptions<JiraOptions> jira,
        ILogger<SlackChannelService> logger,
        IEnumerable<IJiraSlackIdentityResolver>? resolvers = null)
    {
        _slack = slack;
        _mappings = mappings;
        _db = db;
        _options = options.Value;
        _jira = jira.Value;
        _logger = logger;
        _resolvers = resolvers?.ToList() ?? (IReadOnlyList<IJiraSlackIdentityResolver>)Array.Empty<IJiraSlackIdentityResolver>();
    }

    public async Task<ChannelProvisionResult> ProvisionAsync(string workItemKey, bool dryRun = false, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return new("NotConfigured", Detail: "Slack bot token is not configured.");

        var item = await _db.WorkItems.FirstOrDefaultAsync(w => w.Key == workItemKey && !w.IsDeleted, ct);
        if (item is null)
            return new("Skipped", Detail: $"Work item {workItemKey} not found.");

        if (!_options.IsEligible(item.IssueType))
            return new("Skipped", Detail: $"Issue type '{item.IssueType}' is not eligible for a channel.");

        var existing = await GetChannelMappingAsync(workItemKey, ct);
        if (existing is not null)
            return new("AlreadyLinked", existing.ResourceId, existing.DisplayName, "A channel is already linked.");

        if (dryRun)
            return new("DryRun", ChannelName: SlackChannelNaming.Derive(item.Key, item.Summary), Detail: "Would create channel.");

        // Create, resolving name collisions with a deterministic suffix.
        SlackChannel channel;
        string? suffix = null;
        var n = 1;
        while (true)
        {
            var name = SlackChannelNaming.Derive(item.Key, item.Summary, suffix);
            try
            {
                channel = await _slack.CreateChannelAsync(name, ct);
                break;
            }
            catch (SlackApiException ex) when (ex.Error == "name_taken" && n < 50)
            {
                suffix = (++n).ToString();
            }
        }

        await _mappings.LinkAsync(ResourceType.SlackChannel, channel.Id, item.Key, channel.Name, ct);
        await SeedContextAsync(channel.Id, item, ct);

        _logger.LogInformation("Provisioned Slack channel {Channel} ({Id}) for {Key}.", channel.Name, channel.Id, item.Key);
        return new("Created", channel.Id, channel.Name);
    }

    public async Task<ChannelProvisionResult> ArchiveAsync(string workItemKey, bool dryRun = false, CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return new("NotConfigured", Detail: "Slack bot token is not configured.");

        var mapping = await GetChannelMappingAsync(workItemKey, ct);
        if (mapping is null) return new("Skipped", Detail: $"No channel linked to {workItemKey}.");
        if (dryRun) return new("DryRun", mapping.ResourceId, mapping.DisplayName, "Would archive channel.");

        await TryAsync(() => _slack.PostMessageAsync(mapping.ResourceId, $":lock: Closing the channel for *{workItemKey}*.", ct), workItemKey, "closing note");
        await _slack.ArchiveAsync(mapping.ResourceId, ct);
        return new("Archived", mapping.ResourceId, mapping.DisplayName);
    }

    public async Task<ChannelProvisionResult> UnarchiveAsync(string workItemKey, bool dryRun = false, CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return new("NotConfigured", Detail: "Slack bot token is not configured.");

        var mapping = await GetChannelMappingAsync(workItemKey, ct);
        if (mapping is null) return new("Skipped", Detail: $"No channel linked to {workItemKey}.");
        if (dryRun) return new("DryRun", mapping.ResourceId, mapping.DisplayName, "Would unarchive channel.");

        await _slack.UnarchiveAsync(mapping.ResourceId, ct);
        await TryAsync(() => _slack.PostMessageAsync(mapping.ResourceId, $":unlock: *{workItemKey}* reopened.", ct), workItemKey, "reopen note");
        return new("Unarchived", mapping.ResourceId, mapping.DisplayName);
    }

    /// <summary>Event-driven lifecycle + context reflection. Best-effort (the sync path swallows throws).</summary>
    public async Task OnWorkItemChangedAsync(WorkItemChange change, CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return;

        var mapping = await GetChannelMappingAsync(change.Key, ct);
        if (mapping is null) return; // no channel → nothing to reflect
        var channelId = mapping.ResourceId;

        var nowClosed = change.StatusChanged && _options.IsClosed(change.Status);
        var reopened = change.StatusChanged && !_options.IsClosed(change.Status) && _options.IsClosed(change.PreviousStatus ?? "");

        if (nowClosed)
        {
            await TryAsync(() => _slack.PostMessageAsync(channelId, $":lock: *{change.Key}* moved to *{change.Status}* — archiving.", ct), change.Key, "closing note");
            await _slack.ArchiveAsync(channelId, ct);
            return; // an archived channel can't be updated further
        }

        if (reopened)
        {
            await _slack.UnarchiveAsync(channelId, ct);
            await TryAsync(() => _slack.PostMessageAsync(channelId, $":unlock: *{change.Key}* reopened — *{change.Status}*.", ct), change.Key, "reopen note");
        }

        await TryAsync(() => _slack.SetTopicAsync(channelId, BuildTopic(change.Status, change.AssigneeDisplayName, BrowseUrl(change.Key)), ct), change.Key, "topic");
        if (change.StatusChanged)
            await TryAsync(() => _slack.PostMessageAsync(channelId, $"Status → *{change.Status}*", ct), change.Key, "status note");
        if (change.AssigneeChanged)
        {
            await TryAsync(() => _slack.PostMessageAsync(channelId, $"Assignee → {change.AssigneeDisplayName ?? "_unassigned_"}", ct), change.Key, "assignee note");
            await InviteParticipantsAsync(channelId, change.Key,
                new JiraUserRef(change.AssigneeAccountId, change.AssigneeDisplayName, null), reporter: null, ct);
        }
    }

    private async Task SeedContextAsync(string channelId, WorkItem item, CancellationToken ct)
    {
        var url = BrowseUrl(item.Key);
        await TryAsync(() => _slack.SetTopicAsync(channelId, BuildTopic(item.Status, item.AssigneeDisplayName, url), ct), item.Key, "topic");

        var ts = await _slack.PostMessageAsync(channelId, BuildContext(item, url), ct);
        await TryAsync(() => _slack.PinMessageAsync(channelId, ts, ct), item.Key, "pin");

        await InviteParticipantsAsync(channelId, item.Key,
            new JiraUserRef(item.AssigneeAccountId, item.AssigneeDisplayName, null),
            new JiraUserRef(null, item.ReporterDisplayName, null), ct);
    }

    /// <summary>Invite the fixed watcher list plus any resolvable Jira participants. All best-effort
    /// and idempotent; unresolved identities are skipped (not an error).</summary>
    private async Task InviteParticipantsAsync(string channelId, string key, JiraUserRef? assignee, JiraUserRef? reporter, CancellationToken ct)
    {
        if (!_options.AutoInvite) return;

        foreach (var userId in _options.InviteUserIds)
            await TryAsync(() => _slack.InviteAsync(channelId, userId, ct), key, "invite (fixed)");

        foreach (var participant in new[] { assignee, reporter })
        {
            if (participant is null || participant.IsEmpty) continue;
            var slackId = await ResolveAsync(participant, ct);
            if (slackId is not null)
                await TryAsync(() => _slack.InviteAsync(channelId, slackId, ct), key, "invite (participant)");
            else
                _logger.LogDebug("No Slack identity for Jira user {User} on {Key}; skipping invite.",
                    participant.DisplayName ?? participant.AccountId ?? "(unknown)", key);
        }
    }

    /// <summary>Try each registered resolver in order; first non-null wins.</summary>
    private async Task<string?> ResolveAsync(JiraUserRef user, CancellationToken ct)
    {
        foreach (var resolver in _resolvers)
        {
            var id = await resolver.ResolveSlackUserIdAsync(user, ct);
            if (id is not null) return id;
        }
        return null;
    }

    private async Task<ResourceMapping?> GetChannelMappingAsync(string key, CancellationToken ct)
        => (await _mappings.GetMappingsForWorkItemAsync(key, ct))
            .FirstOrDefault(m => m.ResourceType == ResourceType.SlackChannel);

    private string? BrowseUrl(string key)
        => string.IsNullOrWhiteSpace(_jira.BaseUrl) ? null : $"{_jira.BaseUrl!.TrimEnd('/')}/browse/{key}";

    private static string BuildTopic(string status, string? assignee, string? url)
    {
        var topic = $"{status} · {assignee ?? "unassigned"}";
        if (!string.IsNullOrEmpty(url)) topic += $" · {url}";
        return topic.Length > 250 ? topic[..250] : topic;
    }

    private static string BuildContext(WorkItem item, string? url)
    {
        var lines = new List<string>
        {
            $"*{item.Key}* · {item.IssueType} · *{item.Status}*",
            item.Summary,
            "",
            $"Assignee: {item.AssigneeDisplayName ?? "unassigned"}",
        };
        if (!string.IsNullOrEmpty(url)) lines.Add($"Jira: {url}");
        return string.Join("\n", lines);
    }

    private async Task TryAsync(Func<Task> action, string key, string what)
    {
        try { await action(); }
        catch (SlackApiException ex) { _logger.LogWarning("Slack {What} failed for {Key}: {Error}", what, key, ex.Error); }
    }
}
