using Microsoft.Extensions.Logging.Abstractions;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.Sync;

namespace SorryDave.JiraSync.Tests;

public class WorkItemSyncServiceTests
{
    private static JiraIssueData Issue(string key, string status, DateTimeOffset updated) => new()
    {
        Key = key, ProjectKey = "DAVE", IssueType = "Story", Status = status,
        Summary = "summary", Updated = updated
    };

    private static WorkItemSyncService NewService(TestDb db) =>
        new(db.Context, TimeProvider.System, NullLogger<WorkItemSyncService>.Instance);

    [Fact]
    public async Task First_observation_creates_work_item()
    {
        using var db = new TestDb();
        var sut = NewService(db);

        var outcome = await sut.ApplyIssueAsync(Issue("DAVE-1", "To Do", DateTimeOffset.UtcNow));

        Assert.Equal(SyncOutcome.Created, outcome);
        var stored = await db.NewContext().WorkItems.FindAsync("DAVE-1");
        Assert.NotNull(stored);
        Assert.Equal("To Do", stored!.Status);
    }

    [Fact]
    public async Task Newer_event_updates_work_item()
    {
        using var db = new TestDb();
        var sut = NewService(db);
        var t0 = DateTimeOffset.UtcNow;

        await sut.ApplyIssueAsync(Issue("DAVE-1", "To Do", t0));
        var outcome = await sut.ApplyIssueAsync(Issue("DAVE-1", "In Progress", t0.AddMinutes(5)));

        Assert.Equal(SyncOutcome.Updated, outcome);
        Assert.Equal("In Progress", (await db.NewContext().WorkItems.FindAsync("DAVE-1"))!.Status);
    }

    [Fact]
    public async Task Stale_out_of_order_event_is_discarded()
    {
        using var db = new TestDb();
        var sut = NewService(db);
        var t0 = DateTimeOffset.UtcNow;

        await sut.ApplyIssueAsync(Issue("DAVE-1", "In Progress", t0));
        // An older event arrives after the newer one (dropped/re-ordered webhook).
        var outcome = await sut.ApplyIssueAsync(Issue("DAVE-1", "To Do", t0.AddMinutes(-5)));

        Assert.Equal(SyncOutcome.SkippedStale, outcome);
        Assert.Equal("In Progress", (await db.NewContext().WorkItems.FindAsync("DAVE-1"))!.Status);
    }

    [Fact]
    public async Task Deletion_soft_deletes_and_is_retained()
    {
        using var db = new TestDb();
        var sut = NewService(db);
        await sut.ApplyIssueAsync(Issue("DAVE-1", "To Do", DateTimeOffset.UtcNow));

        var outcome = await sut.ApplyDeletionAsync("DAVE-1");

        Assert.Equal(SyncOutcome.Deleted, outcome);
        var stored = await db.NewContext().WorkItems.FindAsync("DAVE-1");
        Assert.NotNull(stored);
        Assert.True(stored!.IsDeleted);
    }
}
