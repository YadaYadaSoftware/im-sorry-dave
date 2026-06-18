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

    public WebhookProcessor(IWorkItemSyncService sync, ILogger<WebhookProcessor> logger)
    {
        _sync = sync;
        _logger = logger;
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

    private static bool TryGetIssue(JsonElement root, out JsonElement issue)
    {
        if (root.TryGetProperty("issue", out issue) && issue.ValueKind == JsonValueKind.Object)
            return true;
        issue = default;
        return false;
    }
}
