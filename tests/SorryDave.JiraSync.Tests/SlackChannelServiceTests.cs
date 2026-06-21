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

        var result = await Service(db, new FakeSlackClient(), eligibleTypes: "Idea").ProvisionAsync("MDP-7");

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

    private static WorkItem Seed(TestDb db, string key = "MDP-7", string type = "Idea",
        string status = "To Do", string summary = "Build Slack Channel")
    {
        var item = new WorkItem
        {
            Key = key,
            ProjectKey = key.Split('-')[0],
            IssueType = type,
            Status = status,
            Summary = summary,
            JiraUpdated = DateTimeOffset.UtcNow,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            LastSyncedUtc = DateTimeOffset.UtcNow,
        };
        db.Context.WorkItems.Add(item);
        db.Context.SaveChanges();
        return item;
    }

    private static SlackChannelService Service(TestDb db, FakeSlackClient slack, params string[] eligibleTypes)
    {
        var slackOptions = new SlackOptions { BotToken = "xoxb-test", EligibleIssueTypes = eligibleTypes.ToList() };
        var jiraOptions = new JiraOptions { BaseUrl = "https://x.atlassian.net" };
        return new SlackChannelService(
            slack,
            new MappingStore(db.Context, TimeProvider.System),
            db.Context,
            Options.Create(slackOptions),
            Options.Create(jiraOptions),
            NullLogger<SlackChannelService>.Instance);
    }

    private sealed class FakeSlackClient : ISlackClient
    {
        public List<string> Created { get; } = new();
        public List<(string Channel, string Text)> Messages { get; } = new();
        public List<string> Archived { get; } = new();
        public List<string> Unarchived { get; } = new();
        public int FailCreateWithNameTaken { get; set; }
        private int _seq;

        public Task<SlackChannel> CreateChannelAsync(string name, CancellationToken ct = default)
        {
            if (FailCreateWithNameTaken-- > 0) throw new SlackApiException("name_taken");
            Created.Add(name);
            return Task.FromResult(new SlackChannel($"C{++_seq}", name, false));
        }

        public Task<SlackChannel?> FindChannelByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<SlackChannel?>(null);

        public Task<string> PostMessageAsync(string channelId, string text, CancellationToken ct = default)
        {
            Messages.Add((channelId, text));
            return Task.FromResult($"ts{++_seq}");
        }

        public Task SetTopicAsync(string channelId, string topic, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetPurposeAsync(string channelId, string purpose, CancellationToken ct = default) => Task.CompletedTask;
        public Task ArchiveAsync(string channelId, CancellationToken ct = default) { Archived.Add(channelId); return Task.CompletedTask; }
        public Task UnarchiveAsync(string channelId, CancellationToken ct = default) { Unarchived.Add(channelId); return Task.CompletedTask; }
        public Task PinMessageAsync(string channelId, string messageTs, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> LookupUserIdByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task InviteAsync(string channelId, string userId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
