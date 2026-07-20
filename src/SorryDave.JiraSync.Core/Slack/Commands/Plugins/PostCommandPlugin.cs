using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Summarization;

namespace SorryDave.JiraSync.Core.Slack.Commands.Plugins;

/// <summary>
/// <c>/post</c> — summarize the conversation since the last <c>/post</c> and post interactive candidate
/// cards, plus the Confirm/Reject buttons those cards carry.
///
/// The command and its buttons are one plugin because confirming is what actually writes back to Jira:
/// disabling <c>/post</c> must disable its Confirm handler too, including on cards already posted.
/// </summary>
public sealed class PostCommandPlugin : ISlackCommandPlugin
{
    public const string CommandName = "post";
    public const string ConfirmAction = "confirm";
    public const string RejectAction = "reject";

    private readonly IConversationSummarizer _summarizer;
    private readonly ISlackClient _slack;
    private readonly SlackOptions _options;

    public PostCommandPlugin(IConversationSummarizer summarizer, ISlackClient slack, IOptions<SlackOptions> options)
    {
        _summarizer = summarizer;
        _slack = slack;
        _options = options.Value;
    }

    public string Name => CommandName;

    public string Description => "Summarize the conversation since the last /post and post candidates for Jira";

    public string AckText => ":hourglass_flowing_sand: Summarizing this channel…";

    public IReadOnlyCollection<string> ActionNames { get; } = new[] { ConfirmAction, RejectAction };

    public async Task<SlackCommandResult> HandleCommandAsync(SlackCommandContext context, CancellationToken ct = default)
    {
        var result = await _summarizer.PostAsync(context.ChannelId, ct);

        if (result.Outcome == "Extracted" && _options.IsConfigured)
            foreach (var c in result.Candidates)
                try { await _slack.PostBlocksAsync(context.ChannelId, CandidateBlocks.Fallback(c), CandidateBlocks.Card(c), ct); }
                catch (SlackApiException) { /* best-effort card posting */ }

        var text = result.Outcome switch
        {
            "NotLinked" => ":warning: This channel isn't linked to a work item.",
            "Empty" => "Nothing new to post since the last `/post`.",
            "Extracted" => $":memo: Posted {result.Candidates.Count} candidate(s) — review and confirm below.",
            _ => result.Detail ?? result.Outcome,
        };

        return SlackCommandResult.Message(text);
    }

    public async Task<SlackActionResult> HandleActionAsync(SlackActionContext context, CancellationToken ct = default)
    {
        if (!Guid.TryParse(context.Value, out var candidateId))
            return SlackActionResult.Message(":x: That candidate is no longer identifiable.");

        var user = context.UserId;
        var outcome = context.ActionName.Equals(ConfirmAction, StringComparison.OrdinalIgnoreCase)
            ? await _summarizer.ConfirmAsync(candidateId, user, ct)
            : await _summarizer.RejectAsync(candidateId, ct);

        var headline = outcome switch
        {
            "Confirmed" => $":white_check_mark: Confirmed and written back to Jira{(user is null ? "" : $" by <@{user}>")}.",
            "Rejected" => ":x: Rejected — not written back.",
            _ => $"Candidate {outcome}.",
        };

        return SlackActionResult.Message(headline);
    }
}
