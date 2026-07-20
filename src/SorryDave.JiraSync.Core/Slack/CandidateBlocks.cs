using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.Slack;

/// <summary>
/// Builds Slack Block Kit payloads for summarization candidates: an interactive card (kind, content,
/// evidence + Confirm/Reject buttons) and the post-action result block that replaces it.
/// </summary>
public static class CandidateBlocks
{
    /// <summary>Action ids are namespaced to the owning command plugin, so the dispatcher can resolve
    /// the owner from the id alone. Cards posted before namespacing carry bare ids, which resolve to no
    /// plugin and are refused — the intended outcome for a stale button on an old message.</summary>
    public const string ConfirmActionId = "post:confirm";

    /// <inheritdoc cref="ConfirmActionId"/>
    public const string RejectActionId = "post:reject";

    /// <summary>Notification fallback text for a candidate card.</summary>
    public static string Fallback(SummaryCandidate c) => $"{c.Kind} candidate for {c.WorkItemKey}: {c.Content}";

    /// <summary>Interactive card: section + context + Confirm/Reject actions carrying the candidate id.</summary>
    public static object[] Card(SummaryCandidate c) => new object[]
    {
        new
        {
            type = "section",
            text = new { type = "mrkdwn", text = $"*{c.Kind}*  ·  _{c.WorkItemKey}_  ·  confidence {c.Confidence:0.00}\n{c.Content}" },
        },
        new
        {
            type = "context",
            elements = new object[] { new { type = "mrkdwn", text = string.IsNullOrWhiteSpace(c.Evidence) ? "_no evidence_" : $"evidence: {c.Evidence}" } },
        },
        new
        {
            type = "actions",
            elements = new object[]
            {
                new { type = "button", text = new { type = "plain_text", text = "Confirm → Jira" }, style = "primary", action_id = ConfirmActionId, value = c.Id.ToString() },
                new { type = "button", text = new { type = "plain_text", text = "Reject" }, style = "danger", action_id = RejectActionId, value = c.Id.ToString() },
            },
        },
    };

    /// <summary>The block that replaces a card once acted on (buttons removed).</summary>
    public static object[] Result(string headline) => new object[]
    {
        new { type = "section", text = new { type = "mrkdwn", text = headline } },
    };
}
