using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Slack;
using SorryDave.JiraSync.Core.Slack.Commands;
using SorryDave.JiraSync.Core.Slack.Commands.Plugins;
using SorryDave.JiraSync.Core.Summarization;

namespace SorryDave.JiraSync.Tests;

/// <summary>
/// Behavioural parity for <c>/post</c> after its relocation behind the plugin contract: the outcomes,
/// messages, and card posting must match what the endpoint produced before the move.
/// </summary>
public class PostCommandPluginTests
{
    private static SummaryCandidate Candidate(string content = "ship Friday") => new()
    {
        Id = Guid.NewGuid(), WorkItemKey = "MDP-7", Kind = WriteBackKind.Decision,
        Content = content, Evidence = "alice", Confidence = 0.9,
    };

    private static (PostCommandPlugin Plugin, StubSummarizer Summarizer, CardCapturingSlackClient Slack) Build(
        PostResult? post = null, bool slackConfigured = true)
    {
        var summarizer = new StubSummarizer { PostResult = post ?? new PostResult("Empty", Array.Empty<SummaryCandidate>()) };
        var slack = new CardCapturingSlackClient();
        var options = new SlackOptions { BotToken = slackConfigured ? "xoxb-test" : null };
        return (new PostCommandPlugin(summarizer, slack, Options.Create(options)), summarizer, slack);
    }

    private static SlackCommandContext Context(string channelId = "C1")
        => new("post", channelId, "U1", "", "https://hooks.example/r", new Dictionary<string, string>());

    // --- Metadata ---

    [Fact]
    public void Declares_its_command_and_owned_actions()
    {
        var (plugin, _, _) = Build();

        Assert.Equal("post", plugin.Name);
        Assert.NotEmpty(plugin.Description);
        Assert.NotEmpty(plugin.AckText);
        Assert.Equal(new[] { "confirm", "reject" }, plugin.ActionNames);
    }

    [Fact]
    public void Owns_the_actions_the_candidate_card_emits()
    {
        // The card's ids and the plugin's owned actions must agree, or every button is refused.
        var (plugin, _, _) = Build();

        Assert.True(SlackActionId.TryParse(CandidateBlocks.ConfirmActionId, out var owner, out var confirm));
        Assert.Equal(plugin.Name, owner);
        Assert.Contains(confirm, plugin.ActionNames);

        Assert.True(SlackActionId.TryParse(CandidateBlocks.RejectActionId, out _, out var reject));
        Assert.Contains(reject, plugin.ActionNames);
    }

    // --- Command outcomes ---

    [Fact]
    public async Task Extracted_posts_a_card_per_candidate_and_reports_the_count()
    {
        var candidates = new[] { Candidate("ship Friday"), Candidate("use sqlite") };
        var (plugin, _, slack) = Build(new PostResult("Extracted", candidates));

        var result = await plugin.HandleCommandAsync(Context("C42"));

        Assert.Equal(":memo: Posted 2 candidate(s) — review and confirm below.", result.Text);
        Assert.Equal(2, slack.Posted.Count);
        Assert.All(slack.Posted, p => Assert.Equal("C42", p.ChannelId));
        Assert.Contains(slack.Posted, p => p.Fallback.Contains("ship Friday"));
    }

    [Fact]
    public async Task NotLinked_warns_and_posts_no_cards()
    {
        var (plugin, _, slack) = Build(new PostResult("NotLinked", Array.Empty<SummaryCandidate>()));

        var result = await plugin.HandleCommandAsync(Context());

        Assert.Equal(":warning: This channel isn't linked to a work item.", result.Text);
        Assert.Empty(slack.Posted);
    }

    [Fact]
    public async Task Empty_reports_nothing_new()
    {
        var (plugin, _, slack) = Build(new PostResult("Empty", Array.Empty<SummaryCandidate>()));

        var result = await plugin.HandleCommandAsync(Context());

        Assert.Equal("Nothing new to post since the last `/post`.", result.Text);
        Assert.Empty(slack.Posted);
    }

    [Fact]
    public async Task An_unknown_outcome_falls_back_to_its_detail()
    {
        var (plugin, _, _) = Build(new PostResult("Throttled", Array.Empty<SummaryCandidate>(), "Try again shortly."));

        Assert.Equal("Try again shortly.", (await plugin.HandleCommandAsync(Context())).Text);
    }

    [Fact]
    public async Task An_unknown_outcome_without_detail_falls_back_to_the_outcome()
    {
        var (plugin, _, _) = Build(new PostResult("Throttled", Array.Empty<SummaryCandidate>()));

        Assert.Equal("Throttled", (await plugin.HandleCommandAsync(Context())).Text);
    }

    [Fact]
    public async Task Posts_no_cards_when_slack_is_not_configured()
    {
        var (plugin, _, slack) = Build(new PostResult("Extracted", new[] { Candidate() }), slackConfigured: false);

        var result = await plugin.HandleCommandAsync(Context());

        Assert.Empty(slack.Posted);
        Assert.Contains("Posted 1 candidate(s)", result.Text);   // still reports the extraction
    }

    [Fact]
    public async Task Card_posting_is_best_effort()
    {
        var (plugin, _, slack) = Build(new PostResult("Extracted", new[] { Candidate(), Candidate() }));
        slack.ThrowOnPost = true;

        var result = await plugin.HandleCommandAsync(Context());   // must not throw

        Assert.Contains("Posted 2 candidate(s)", result.Text);
    }

    [Fact]
    public async Task Summarizes_the_invoked_channel()
    {
        var (plugin, summarizer, _) = Build();

        await plugin.HandleCommandAsync(Context("C99"));

        Assert.Equal("C99", summarizer.PostedChannelId);
    }

    // --- Actions ---

    [Fact]
    public async Task Confirm_writes_back_and_attributes_the_confirming_user()
    {
        var (plugin, summarizer, _) = Build();
        summarizer.ConfirmOutcome = "Confirmed";
        var id = Guid.NewGuid();

        var result = await plugin.HandleActionAsync(
            new SlackActionContext("confirm", id.ToString(), "U7", "C1", null));

        Assert.Equal(id, summarizer.ConfirmedId);
        Assert.Equal("U7", summarizer.ConfirmingUser);
        Assert.Equal(":white_check_mark: Confirmed and written back to Jira by <@U7>.", result.Headline);
    }

    [Fact]
    public async Task Confirm_without_a_known_user_omits_the_attribution()
    {
        var (plugin, summarizer, _) = Build();
        summarizer.ConfirmOutcome = "Confirmed";

        var result = await plugin.HandleActionAsync(
            new SlackActionContext("confirm", Guid.NewGuid().ToString(), null, "C1", null));

        Assert.Equal(":white_check_mark: Confirmed and written back to Jira.", result.Headline);
    }

    [Fact]
    public async Task Reject_does_not_write_back()
    {
        var (plugin, summarizer, _) = Build();
        summarizer.RejectOutcome = "Rejected";
        var id = Guid.NewGuid();

        var result = await plugin.HandleActionAsync(
            new SlackActionContext("reject", id.ToString(), "U7", "C1", null));

        Assert.Equal(id, summarizer.RejectedId);
        Assert.Null(summarizer.ConfirmedId);
        Assert.Equal(":x: Rejected — not written back.", result.Headline);
    }

    [Fact]
    public async Task An_unexpected_outcome_is_reported_verbatim()
    {
        var (plugin, summarizer, _) = Build();
        summarizer.ConfirmOutcome = "AlreadySent";

        var result = await plugin.HandleActionAsync(
            new SlackActionContext("confirm", Guid.NewGuid().ToString(), "U7", "C1", null));

        Assert.Equal("Candidate AlreadySent.", result.Headline);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task An_unidentifiable_candidate_is_refused_without_touching_the_summarizer(string? value)
    {
        var (plugin, summarizer, _) = Build();

        var result = await plugin.HandleActionAsync(new SlackActionContext("confirm", value, "U7", "C1", null));

        Assert.Contains("no longer identifiable", result.Headline);
        Assert.Null(summarizer.ConfirmedId);
        Assert.Null(summarizer.RejectedId);
    }

    // --- Doubles ---

    private sealed class StubSummarizer : IConversationSummarizer
    {
        public PostResult PostResult { get; set; } = new("Empty", Array.Empty<SummaryCandidate>());
        public string? PostedChannelId { get; private set; }
        public Guid? ConfirmedId { get; private set; }
        public string? ConfirmingUser { get; private set; }
        public Guid? RejectedId { get; private set; }
        public string ConfirmOutcome { get; set; } = "Confirmed";
        public string RejectOutcome { get; set; } = "Rejected";

        public Task CaptureAsync(IncomingMessage message, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PostResult> PostAsync(string channelId, CancellationToken ct = default)
        {
            PostedChannelId = channelId;
            return Task.FromResult(PostResult);
        }

        public Task<PostResult> SmokeSummarizeAsync(string workItemKey, IReadOnlyList<TranscriptLine> lines, CancellationToken ct = default)
            => Task.FromResult(PostResult);

        public Task<string> ConfirmAsync(Guid candidateId, string? confirmingUser, CancellationToken ct = default)
        {
            ConfirmedId = candidateId;
            ConfirmingUser = confirmingUser;
            return Task.FromResult(ConfirmOutcome);
        }

        public Task<string> RejectAsync(Guid candidateId, CancellationToken ct = default)
        {
            RejectedId = candidateId;
            return Task.FromResult(RejectOutcome);
        }
    }

    /// <summary>Records the candidate cards posted; only <c>PostBlocksAsync</c> is exercised by /post.</summary>
    private sealed class CardCapturingSlackClient : ISlackClient
    {
        public List<(string ChannelId, string Fallback)> Posted { get; } = new();
        public bool ThrowOnPost { get; set; }

        public Task<string> PostBlocksAsync(string channelId, string fallbackText, object blocks, CancellationToken ct = default)
        {
            if (ThrowOnPost) throw new SlackApiException("channel_not_found");
            Posted.Add((channelId, fallbackText));
            return Task.FromResult("1.0");
        }

        public Task<SlackChannel> CreateChannelAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SlackChannel?> FindChannelByNameAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SlackChannel?> GetChannelInfoAsync(string channelId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> PostMessageAsync(string channelId, string text, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetTopicAsync(string channelId, string topic, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetPurposeAsync(string channelId, string purpose, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ArchiveAsync(string channelId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnarchiveAsync(string channelId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task PinMessageAsync(string channelId, string messageTs, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> LookupUserIdByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> InviteAsync(string channelId, string userId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
