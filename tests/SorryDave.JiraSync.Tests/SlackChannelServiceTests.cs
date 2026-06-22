using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Mapping;
using SorryDave.JiraSync.Core.Slack;
using SorryDave.JiraSync.Core.Sync;

namespace SorryDave.JiraSync.Tests;

public class SlackChannelServiceTests
{
    [Fact]
    public async Task Provision_creates_channel_links_it_and_posts_context()
    {
        using var db = new TestDb();
        Seed(db);
        var slack = new FakeSlackClient();

        var result = await Service(db, slack).ProvisionAsync("MDP-7");

        Assert.Equal("Created", result.Outcome);
        Assert.Equal("mdp-7-build-slack-channel", result.ChannelName);
        Assert.Single(slack.Created);
        Assert.Contains(slack.Messages, m => m.Text.Contains("MDP-7"));

        var resolved = await new MappingStore(db.NewContext(), TimeProvider.System)
            .ResolveByResourceAsync(ResourceType.SlackChannel, result.ChannelId!);
        Assert.Equal("MDP-7", resolved!.Key);
    }

    [Fact]
    public async Task Provision_is_idempotent_when_already_linked()
    {
        using var db = new TestDb();
        Seed(db);
        var slack = new FakeSlackClient();
        var svc = Service(db, slack);

        await svc.ProvisionAsync("MDP-7");
        var second = await svc.ProvisionAsync("MDP-7");

        Assert.Equal("AlreadyLinked", second.Outcome);
        Assert.Single(slack.Created); // no duplicate channel
    }

    [Fact]
    public async Task Provision_resolves_name_collision_with_a_deterministic_suffix()
    {
        using var db = new TestDb();
        Seed(db);
        var slack = new FakeSlackClient { FailCreateWithNameTaken = 1 };

        var result = await Service(db, slack).ProvisionAsync("MDP-7");

        Assert.Equal("Created", result.Outcome);
        Assert.Equal("mdp-7-build-slack-channel-2", result.ChannelName);
    }

    [Fact]
    public async Task Provision_skips_ineligible_issue_type()
    {
        using var db = new TestDb();
        Seed(db, type: "Bug");

        var result = await Service(db, new FakeSlackClient(), new SlackOptions { EligibleIssueTypes = { "Idea" } }).ProvisionAsync("MDP-7");

        Assert.Equal("Skipped", result.Outcome);
    }

    [Fact]
    public async Task Status_change_to_closed_archives_the_channel()
    {
        using var db = new TestDb();
        Seed(db);
        var slack = new FakeSlackClient();
        var svc = Service(db, slack);
        var prov = await svc.ProvisionAsync("MDP-7");

        await svc.OnWorkItemChangedAsync(new WorkItemChange { Key = "MDP-7", Status = "Done", PreviousStatus = "To Do" });

        Assert.Contains(prov.ChannelId, slack.Archived);
    }

    [Fact]
    public async Task Reopen_unarchives_the_channel()
    {
        using var db = new TestDb();
        Seed(db);
        var slack = new FakeSlackClient();
        var svc = Service(db, slack);
        var prov = await svc.ProvisionAsync("MDP-7");

        await svc.OnWorkItemChangedAsync(new WorkItemChange { Key = "MDP-7", Status = "To Do", PreviousStatus = "Done" });

        Assert.Contains(prov.ChannelId, slack.Unarchived);
    }

    [Fact]
    public async Task Provision_invites_fixed_list_and_resolved_assignee_and_reporter()
    {
        using var db = new TestDb();
        Seed(db, assigneeAccountId: "acc-assignee", assigneeDisplayName: "Dave", reporterDisplayName: "Frank");
        var slack = new FakeSlackClient();
        var options = new SlackOptions
        {
            BotToken = "xoxb-test",
            InviteUserIds = { "U-watcher" },
            UserMap = { ["acc-assignee"] = "U-dave", ["Frank"] = "U-frank" },
        };

        var result = await Service(db, slack, options).ProvisionAsync("MDP-7");

        var invited = slack.Invited.Where(i => i.Channel == result.ChannelId).Select(i => i.User).ToList();
        Assert.Contains("U-watcher", invited); // fixed list
        Assert.Contains("U-dave", invited);    // assignee resolved by accountId
        Assert.Contains("U-frank", invited);   // reporter resolved by displayName
    }

    [Fact]
    public async Task Unresolved_participant_is_skipped_without_failing()
    {
        using var db = new TestDb();
        Seed(db, assigneeAccountId: "acc-unknown", assigneeDisplayName: "Nobody");
        var slack = new FakeSlackClient();

        var result = await Service(db, slack, new SlackOptions { BotToken = "xoxb-test" }).ProvisionAsync("MDP-7");

        Assert.Equal("Created", result.Outcome);  // provisioning still succeeds
        Assert.Empty(slack.Invited);               // nobody mapped → nobody invited
    }

    [Fact]
    public async Task Assignee_change_invites_the_new_assignee()
    {
        using var db = new TestDb();
        Seed(db);
        var slack = new FakeSlackClient();
        var options = new SlackOptions { BotToken = "xoxb-test", UserMap = { ["acc-new"] = "U-new" } };
        var svc = Service(db, slack, options);
        var prov = await svc.ProvisionAsync("MDP-7");

        await svc.OnWorkItemChangedAsync(new WorkItemChange
        {
            Key = "MDP-7",
            Status = "To Do",
            PreviousStatus = "To Do",
            AssigneeAccountId = "acc-new",
            AssigneeDisplayName = "Newbie",
            PreviousAssigneeAccountId = "acc-old",
        });

        Assert.Contains((prov.ChannelId!, "U-new"), slack.Invited);
    }

    [Fact]
    public async Task Reconcile_archives_the_channel_when_the_item_is_closed()
    {
        using var db = new TestDb();
        var item = Seed(db);
        var slack = new FakeSlackClient();
        var svc = Service(db, slack);
        var prov = await svc.ProvisionAsync("MDP-7");

        item.Status = "Done"; // closed out-of-band (no listener fired)
        db.Context.SaveChanges();

        var drift = await svc.ReconcileLinksAsync();

        Assert.Equal(1, drift);
        Assert.Contains(prov.ChannelId, slack.Archived);
    }

    [Fact]
    public async Task Reconcile_flags_a_dangling_link_when_the_channel_is_gone()
    {
        using var db = new TestDb();
        Seed(db);
        var slack = new FakeSlackClient();
        var svc = Service(db, slack);
        var prov = await svc.ProvisionAsync("MDP-7");

        slack.MissingChannels.Add(prov.ChannelId!); // deleted in Slack

        var drift = await svc.ReconcileLinksAsync();

        Assert.Equal(1, drift);
        Assert.DoesNotContain(prov.ChannelId, slack.Archived); // nothing to archive — just flagged
    }

    [Fact]
    public async Task Reconcile_unarchives_the_channel_when_the_item_is_open()
    {
        using var db = new TestDb();
        Seed(db); // status "To Do" (open)
        var slack = new FakeSlackClient();
        var svc = Service(db, slack);
        var prov = await svc.ProvisionAsync("MDP-7");

        await slack.ArchiveAsync(prov.ChannelId!); // archived out-of-band

        var drift = await svc.ReconcileLinksAsync();

        Assert.Equal(1, drift);
        Assert.Contains(prov.ChannelId, slack.Unarchived);
    }

    private static WorkItem Seed(TestDb db, string key = "MDP-7", string type = "Idea",
        string status = "To Do", string summary = "Build Slack Channel",
        string? assigneeAccountId = null, string? assigneeDisplayName = null, string? reporterDisplayName = null)
    {
        var item = new WorkItem
        {
            Key = key,
            ProjectKey = key.Split('-')[0],
            IssueType = type,
            Status = status,
            Summary = summary,
            AssigneeAccountId = assigneeAccountId,
            AssigneeDisplayName = assigneeDisplayName,
            ReporterDisplayName = reporterDisplayName,
            JiraUpdated = DateTimeOffset.UtcNow,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            LastSyncedUtc = DateTimeOffset.UtcNow,
        };
        db.Context.WorkItems.Add(item);
        db.Context.SaveChanges();
        return item;
    }

    private static SlackChannelService Service(TestDb db, FakeSlackClient slack, SlackOptions? options = null)
    {
        var slackOptions = options ?? new SlackOptions();
        if (string.IsNullOrEmpty(slackOptions.BotToken)) slackOptions.BotToken = "xoxb-test";
        var jiraOptions = new JiraOptions { BaseUrl = "https://x.atlassian.net" };
        var resolvers = new IJiraSlackIdentityResolver[] { new ConfigMapIdentityResolver(Options.Create(slackOptions)) };
        return new SlackChannelService(
            slack,
            new MappingStore(db.Context, TimeProvider.System),
            db.Context,
            Options.Create(slackOptions),
            Options.Create(jiraOptions),
            NullLogger<SlackChannelService>.Instance,
            resolvers);
    }

    private sealed class FakeSlackClient : ISlackClient
    {
        public List<string> Created { get; } = new();
        public List<(string Channel, string Text)> Messages { get; } = new();
        public List<string> Archived { get; } = new();
        public List<string> Unarchived { get; } = new();
        public List<(string Channel, string User)> Invited { get; } = new();
        public int FailCreateWithNameTaken { get; set; }
        /// <summary>Channel ids that GetChannelInfo should report as gone (simulate out-of-band deletion).</summary>
        public HashSet<string> MissingChannels { get; } = new();
        private readonly Dictionary<string, (string Name, bool Archived)> _channels = new();
        private int _seq;

        public Task<SlackChannel> CreateChannelAsync(string name, CancellationToken ct = default)
        {
            if (FailCreateWithNameTaken-- > 0) throw new SlackApiException("name_taken");
            Created.Add(name);
            var id = $"C{++_seq}";
            _channels[id] = (name, false);
            return Task.FromResult(new SlackChannel(id, name, false));
        }

        public Task<SlackChannel?> FindChannelByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<SlackChannel?>(null);

        public Task<SlackChannel?> GetChannelInfoAsync(string channelId, CancellationToken ct = default)
        {
            if (MissingChannels.Contains(channelId) || !_channels.TryGetValue(channelId, out var c))
                return Task.FromResult<SlackChannel?>(null);
            return Task.FromResult<SlackChannel?>(new SlackChannel(channelId, c.Name, c.Archived));
        }

        public Task<string> PostMessageAsync(string channelId, string text, CancellationToken ct = default)
        {
            Messages.Add((channelId, text));
            return Task.FromResult($"ts{++_seq}");
        }

        public Task SetTopicAsync(string channelId, string topic, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetPurposeAsync(string channelId, string purpose, CancellationToken ct = default) => Task.CompletedTask;
        public Task ArchiveAsync(string channelId, CancellationToken ct = default)
        {
            Archived.Add(channelId);
            if (_channels.TryGetValue(channelId, out var c)) _channels[channelId] = (c.Name, true);
            return Task.CompletedTask;
        }
        public Task UnarchiveAsync(string channelId, CancellationToken ct = default)
        {
            Unarchived.Add(channelId);
            if (_channels.TryGetValue(channelId, out var c)) _channels[channelId] = (c.Name, false);
            return Task.CompletedTask;
        }
        public Task PinMessageAsync(string channelId, string messageTs, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> LookupUserIdByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task InviteAsync(string channelId, string userId, CancellationToken ct = default) { Invited.Add((channelId, userId)); return Task.CompletedTask; }
    }
}
