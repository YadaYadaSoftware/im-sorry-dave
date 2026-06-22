using System.Text.Json;
using Microsoft.Extensions.Logging;
using SorryDave.JiraSync.Core.Jira;

namespace SorryDave.JiraSync.Core.Sync;

public record WebhookResult(string Event, SyncOutcome? Outcome, string? IssueKey, string? Note = null);

/// <summary>
/// Interprets raw Jira webhook payloads and routes them to the sync service. Recognises
/// issue created/updated/deleted and comment created events; anything else is acknowledged
/// and ignored.
/// </summary>
public class WebhookProcessor
{
    private readonly IWorkItemSyncService _sync;
    private readonly ILogger<WebhookProcessor> _logger;
    private readonly IReadOnlyList<IWorkItemChangeListener> _listeners;
    private readonly IJiraClient? _jira;

    public WebhookProcessor(
        IWorkItemSyncService sync,
        ILogger<WebhookProcessor> logger,
        IEnumerable<IWorkItemChangeListener>? listeners = null,
        IJiraClient? jira = null)
    {
        _sync = sync;
        _logger = logger;
        _listeners = listeners?.ToList() ?? (IReadOnlyList<IWorkItemChangeListener>)Array.Empty<IWorkItemChangeListener>();
        _jira = jira;
    }

    public async Task<WebhookResult> ProcessAsync(JsonElement root, CancellationToken ct = default)
    {
        var evt = root.TryGetProperty("webhookEvent", out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()!
            : "(unknown)";

        switch (evt)
        {
            case "jira:issue_created":
            case "jira:issue_updated":
            case "comment_created":
            case "comment_updated":
            {
                if (!TryGetIssue(root, out var issueElement))
                    return new WebhookResult(evt, null, null, "no issue in payload");

                var data = JiraIssueParser.Parse(issueElement);
                var outcome = await _sync.ApplyIssueAsync(data, ct);

                // Comment events: invite anyone @mentioned in the comment. The webhook body may be
                // rendered text (no accountIds), so prefer the ADF in the payload, then fall back to
                // fetching the comment via REST (guaranteed ADF). Best-effort, via the listener seam.
                if (evt is "comment_created" or "comment_updated" &&
                    root.TryGetProperty("comment", out var comment) &&
                    comment.ValueKind == JsonValueKind.Object)
                {
                    var (mentions, text) = await ResolveCommentMentionsAsync(data.Key, comment, ct);
                    if (mentions.Count > 0)
                        await NotifyAsync(new WorkItemChange
                        {
                            Key = data.Key,
                            Status = data.Status,
                            PreviousStatus = data.Status, // no status transition for a comment
                            MentionedAccountIds = mentions.ToList(),
                            MentionContext = text,
                        }, ct);
                }

                return new WebhookResult(evt, outcome, data.Key);
            }
            case "jira:issue_deleted":
            {
                if (!TryGetIssue(root, out var issueElement) ||
                    !issueElement.TryGetProperty("key", out var keyEl))
                    return new WebhookResult(evt, null, null, "no issue key in payload");

                var key = keyEl.GetString()!;
                var outcome = await _sync.ApplyDeletionAsync(key, ct);
                return new WebhookResult(evt, outcome, key);
            }
            default:
                _logger.LogDebug("Ignoring unhandled webhook event '{Event}'.", evt);
                return new WebhookResult(evt, null, null, "ignored");
        }
    }

    /// <summary>Extract mention accountIds and the readable text from the comment: prefer ADF in the
    /// webhook payload, then fall back to fetching the comment via REST (the webhook often renders the
    /// body as plain text, which carries the @name but not the accountId we need to resolve).</summary>
    private async Task<(IReadOnlyList<string> Mentions, string? Text)> ResolveCommentMentionsAsync(string issueKey, JsonElement comment, CancellationToken ct)
    {
        var hasBody = comment.TryGetProperty("body", out var body);
        var bodyKind = hasBody ? body.ValueKind.ToString() : "(none)";

        if (hasBody && body.ValueKind == JsonValueKind.Object)
        {
            var fromPayload = AdfText.CollectMentionAccountIds(body);
            if (fromPayload.Count > 0) return (fromPayload, AdfText.Flatten(body));
        }

        // No accountIds in the payload (rendered string, or none) — fetch the ADF comment by id.
        if (_jira is not null &&
            comment.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            var fetched = await _jira.GetCommentAsync(issueKey, idEl.GetString()!, ct);
            _logger.LogInformation(
                "Comment on {Key}: webhook body kind={BodyKind}, mentions via fetch={Count}.",
                issueKey, bodyKind, fetched.MentionAccountIds.Count);
            return (fetched.MentionAccountIds, fetched.Text);
        }

        // Plain-string webhook body: use it as the welcome text even though we found no accountIds.
        var fallbackText = hasBody && body.ValueKind == JsonValueKind.String ? body.GetString() : null;
        _logger.LogInformation("Comment on {Key}: webhook body kind={BodyKind}, no fetch available.", issueKey, bodyKind);
        return (Array.Empty<string>(), fallbackText);
    }

    /// <summary>Best-effort fan-out to change listeners — failures logged, never rethrown.</summary>
    private async Task NotifyAsync(WorkItemChange change, CancellationToken ct)
    {
        foreach (var listener in _listeners)
        {
            try { await listener.OnWorkItemChangedAsync(change, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Comment-mention listener {Listener} failed for {Key}.",
                    listener.GetType().Name, change.Key);
            }
        }
    }

    private static bool TryGetIssue(JsonElement root, out JsonElement issue)
    {
        if (root.TryGetProperty("issue", out issue) && issue.ValueKind == JsonValueKind.Object)
            return true;
        issue = default;
        return false;
    }
}
