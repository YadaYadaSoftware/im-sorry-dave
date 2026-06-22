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

    public WebhookProcessor(
        IWorkItemSyncService sync,
        ILogger<WebhookProcessor> logger,
        IEnumerable<IWorkItemChangeListener>? listeners = null)
    {
        _sync = sync;
        _logger = logger;
        _listeners = listeners?.ToList() ?? (IReadOnlyList<IWorkItemChangeListener>)Array.Empty<IWorkItemChangeListener>();
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

                // Comment events: invite anyone @mentioned in the comment body (the comment is only
                // in the webhook root, not the issue). Best-effort, via the change-listener seam.
                if (evt is "comment_created" or "comment_updated" &&
                    root.TryGetProperty("comment", out var comment) &&
                    comment.ValueKind == JsonValueKind.Object &&
                    comment.TryGetProperty("body", out var body))
                {
                    var mentions = AdfText.CollectMentionAccountIds(body);
                    if (mentions.Count > 0)
                        await NotifyAsync(new WorkItemChange
                        {
                            Key = data.Key,
                            Status = data.Status,
                            PreviousStatus = data.Status, // no status transition for a comment
                            MentionedAccountIds = mentions,
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
