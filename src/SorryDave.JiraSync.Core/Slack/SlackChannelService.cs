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

        // Idempotency: if a channel with our derived name already exists in Slack and is NOT mapped to
        // another work item (an orphan from a prior run/race/mirror-rebuild), adopt it instead of
        // creating a duplicate — honoring "no duplicate channel created".
        var baseName = SlackChannelNaming.Derive(item.Key, item.Summary);
        var existingByName = await _slack.FindChannelByNameAsync(baseName, ct);
        if (existingByName is not null)
        {
            var owner = await _mappings.ResolveByResourceAsync(ResourceType.SlackChannel, existingByName.Id, ct);
            if (owner is null || owner.Key == item.Key)
            {
                await _mappings.LinkAsync(ResourceType.SlackChannel, existingByName.Id, item.Key, existingByName.Name, ct);
                _logger.LogInformation("Adopted existing channel {Channel} for {Key} (no duplicate created).", existingByName.Name, item.Key);
                return new("Adopted", existingByName.Id, existingByName.Name);
            }
            // Owned by a different work item → genuine name collision; fall through to a suffixed create.
        }

        // Create, resolving genuine name collisions (a different item) with a deterministic suffix.
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

    public async Task<int> ReconcileLinksAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return 0;

        var mappings = await _db.ResourceMappings
            .Where(m => m.ResourceType == ResourceType.SlackChannel)
            .ToListAsync(ct);

        var drift = 0;
        foreach (var mapping in mappings)
        {
            var info = await _slack.GetChannelInfoAsync(mapping.ResourceId, ct);
            if (info is null)
            {
                _logger.LogWarning("Slack channel {Id} linked to {Key} no longer exists (dangling link).",
                    mapping.ResourceId, mapping.WorkItemKey);
                drift++;
                continue;
            }

            var item = await _db.WorkItems.FirstOrDefaultAsync(w => w.Key == mapping.WorkItemKey, ct);
            if (item is null) continue;

            var shouldBeArchived = item.IsDeleted || _options.IsClosed(item.Status);
            if (shouldBeArchived && !info.IsArchived)
            {
                _logger.LogInformation("Reconcile: archiving {Channel} for closed {Key}.", info.Name, item.Key);
                await TryAsync(() => _slack.ArchiveAsync(mapping.ResourceId, ct), item.Key, "reconcile-archive");
                drift++;
            }
            else if (!shouldBeArchived && info.IsArchived)
            {
                _logger.LogInformation("Reconcile: unarchiving {Channel} for open {Key}.", info.Name, item.Key);
                await TryAsync(() => _slack.UnarchiveAsync(mapping.ResourceId, ct), item.Key, "reconcile-unarchive");
                drift++;
            }
        }

        if (drift > 0) _logger.LogInformation("Slack reconcile: {Drift} drift item(s) found/addressed.", drift);
        return drift;
    }

    /// <summary>Event-driven lifecycle + context reflection. Best-effort (the sync path swallows throws).</summary>
    public async Task OnWorkItemChangedAsync(WorkItemChange change, CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return;

        // A newly created work item auto-provisions its channel (eligible types only); ProvisionAsync
        // is idempotent and seeds context + invites, so we're done.
        if (change.Created)
        {
            await ProvisionAsync(change.Key, dryRun: false, ct);
            return;
        }

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
                new[] { new JiraUserRef(change.AssigneeAccountId, change.AssigneeDisplayName, null) }, ct);
        }

        // Invite anyone newly @mentioned (in a description edit or a comment) and welcome the ones
        // actually added with the text of the mention (their welcome message into this channel).
        if (change.MentionedAccountIds.Count > 0)
        {
            var added = await InviteParticipantsAsync(channelId, change.Key,
                change.MentionedAccountIds.Select(id => new JiraUserRef(id, null, null)), ct);
            if (added.Count > 0 && !string.IsNullOrWhiteSpace(change.MentionContext))
            {
                var who = string.Join(" ", added.Select(u => $"<@{u}>"));
                await TryAsync(() => _slack.PostMessageAsync(channelId,
                    $"{who} you were mentioned on *{change.Key}*:\n> {Truncate(change.MentionContext!.Trim(), 1000)}", ct),
                    change.Key, "mention welcome");
            }
        }
    }

    private async Task SeedContextAsync(string channelId, WorkItem item, CancellationToken ct)
    {
        var url = BrowseUrl(item.Key);
        await TryAsync(() => _slack.SetTopicAsync(channelId, BuildTopic(item.Status, item.AssigneeDisplayName, url), ct), item.Key, "topic");

        // Two welcome messages: a pinned header (title + Jira link), then the description.
        var headerTs = await _slack.PostMessageAsync(channelId, BuildHeader(item, url), ct);
        await TryAsync(() => _slack.PinMessageAsync(channelId, headerTs, ct), item.Key, "pin");
        if (!string.IsNullOrWhiteSpace(item.Description))
            await TryAsync(() => _slack.PostMessageAsync(channelId, Truncate(item.Description!, 3000), ct), item.Key, "description");

        // Provision time: invite the fixed watcher list once, plus the assignee, the creator
        // (reporter), and everyone @mentioned in the description.
        if (_options.AutoInvite)
            foreach (var userId in _options.InviteUserIds)
                await TryAsync(() => _slack.InviteAsync(channelId, userId, ct), item.Key, "invite (fixed)");

        var participants = new List<JiraUserRef>
        {
            new(item.AssigneeAccountId, item.AssigneeDisplayName, null),
            new(item.ReporterAccountId, item.ReporterDisplayName, null),
        };
        participants.AddRange(item.MentionedAccountIds.Select(id => new JiraUserRef(id, null, null)));
        await InviteParticipantsAsync(channelId, item.Key, participants, ct);
    }

    /// <summary>Resolve and invite specific Jira participants (not the fixed watcher list). Returns
    /// the Slack ids that were newly added (not already members). Best-effort and idempotent;
    /// unresolved identities are skipped (not an error).</summary>
    private async Task<List<string>> InviteParticipantsAsync(string channelId, string key, IEnumerable<JiraUserRef> participants, CancellationToken ct)
    {
        var newlyAdded = new List<string>();
        if (!_options.AutoInvite) return newlyAdded;

        foreach (var participant in participants)
        {
            if (participant.IsEmpty) continue;
            var slackId = await ResolveAsync(participant, ct);
            if (slackId is null)
            {
                _logger.LogDebug("No Slack identity for Jira user {User} on {Key}; skipping invite.",
                    participant.DisplayName ?? participant.AccountId ?? "(unknown)", key);
                continue;
            }
            try
            {
                if (await _slack.InviteAsync(channelId, slackId, ct)) newlyAdded.Add(slackId);
            }
            catch (SlackApiException ex)
            {
                _logger.LogWarning("Slack invite failed for {Key}: {Error}", key, ex.Error);
            }
        }
        return newlyAdded;
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

    // Built from the site URL, not the API base — those differ when a scoped token routes the REST
    // client through the API gateway, and gateway URLs are not browsable.
    private string? BrowseUrl(string key)
        => string.IsNullOrWhiteSpace(_jira.EffectiveSiteUrl)
            ? null
            : $"{_jira.EffectiveSiteUrl!.TrimEnd('/')}/browse/{key}";

    private static string BuildTopic(string status, string? assignee, string? url)
    {
        var topic = $"{status} · {assignee ?? "unassigned"}";
        if (!string.IsNullOrEmpty(url)) topic += $" · {url}";
        return topic.Length > 250 ? topic[..250] : topic;
    }

    /// <summary>The pinned header: title (key/type/status + summary) and a link to Jira.</summary>
    private static string BuildHeader(WorkItem item, string? url)
    {
        var lines = new List<string>
        {
            $"*{item.Key}* · {item.IssueType} · *{item.Status}*",
            item.Summary,
        };
        if (!string.IsNullOrEmpty(url)) lines.Add($"Jira: {url}");
        return string.Join("\n", lines);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private async Task TryAsync(Func<Task> action, string key, string what)
    {
        try { await action(); }
        catch (SlackApiException ex) { _logger.LogWarning("Slack {What} failed for {Key}: {Error}", what, key, ex.Error); }
    }
}
