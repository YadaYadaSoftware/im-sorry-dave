using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.Sync;

namespace SorryDave.JiraSync.Tests;

public class MentionNotificationTests
{
    private sealed class CapturingListener : IWorkItemChangeListener
    {
        public List<WorkItemChange> Changes { get; } = new();
        public Task OnWorkItemChangedAsync(WorkItemChange change, CancellationToken ct = default)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }

    private static JiraIssueData Issue(string key, string updated, List<string>? mentions = null) => new()
    {
        Key = key,
        ProjectKey = "MDP",
        IssueType = "Idea",
        Status = "To Do",
        Summary = "s",
        Updated = DateTimeOffset.Parse(updated),
        MentionedAccountIds = mentions ?? new(),
    };

    [Fact]
    public async Task Description_edit_adding_a_mention_notifies_with_the_new_mention()
    {
        using var db = new TestDb();
        var listener = new CapturingListener();
        var sync = new WorkItemSyncService(db.Context, TimeProvider.System, NullLogger<WorkItemSyncService>.Instance,
            new[] { listener });

        await sync.ApplyIssueAsync(Issue("MDP-7", "2026-06-22T10:00:00Z"));               // create, no mentions
        await sync.ApplyIssueAsync(Issue("MDP-7", "2026-06-22T11:00:00Z", new() { "acc-chris" })); // edit adds a mention

        var update = listener.Changes.Last();
        Assert.False(update.Created);
        Assert.Contains("acc-chris", update.MentionedAccountIds);
    }

    [Fact]
    public async Task Comment_created_webhook_notifies_listeners_with_comment_body_mentions()
    {
        using var db = new TestDb();
        var listener = new CapturingListener();
        var sync = new WorkItemSyncService(db.Context, TimeProvider.System, NullLogger<WorkItemSyncService>.Instance);
        var processor = new WebhookProcessor(sync, NullLogger<WebhookProcessor>.Instance, new[] { listener });

        var payload = """
        {
          "webhookEvent": "comment_created",
          "issue": { "key": "MDP-7", "fields": {
            "project": { "key": "MDP" }, "issuetype": { "name": "Idea" },
            "status": { "name": "To Do" }, "summary": "s", "updated": "2026-06-22T10:00:00.000+0000"
          } },
          "comment": { "body": {
            "type": "doc", "version": 1, "content": [
              { "type": "paragraph", "content": [
                { "type": "text", "text": "cc " },
                { "type": "mention", "attrs": { "id": "acc-chris", "text": "@Chris" } }
              ] }
            ]
          } }
        }
        """;

        await processor.ProcessAsync(JsonDocument.Parse(payload).RootElement);

        var notified = listener.Changes.SingleOrDefault(c => c.MentionedAccountIds.Count > 0);
        Assert.NotNull(notified);
        Assert.Equal("MDP-7", notified!.Key);
        Assert.Contains("acc-chris", notified.MentionedAccountIds);
    }
}
