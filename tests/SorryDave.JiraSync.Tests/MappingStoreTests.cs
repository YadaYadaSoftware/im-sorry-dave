using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Mapping;

namespace SorryDave.JiraSync.Tests;

public class MappingStoreTests
{
    private static async Task Seed(TestDb db, params string[] keys)
    {
        foreach (var key in keys)
            db.Context.WorkItems.Add(new WorkItem
            {
                Key = key, ProjectKey = "DAVE", IssueType = "Story", Status = "To Do",
                Summary = "s", JiraUpdated = DateTimeOffset.UtcNow,
                FirstSeenUtc = DateTimeOffset.UtcNow, LastSyncedUtc = DateTimeOffset.UtcNow
            });
        await db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Link_then_resolve_returns_work_item()
    {
        using var db = new TestDb();
        await Seed(db, "DAVE-1");
        var sut = new MappingStore(db.Context, TimeProvider.System);

        await sut.LinkAsync(ResourceType.SlackChannel, "C123", "DAVE-1", "dave-1-doors");
        var resolved = await sut.ResolveByResourceAsync(ResourceType.SlackChannel, "C123");

        Assert.NotNull(resolved);
        Assert.Equal("DAVE-1", resolved!.Key);
    }

    [Fact]
    public async Task Relinking_same_pair_is_idempotent()
    {
        using var db = new TestDb();
        await Seed(db, "DAVE-1");
        var sut = new MappingStore(db.Context, TimeProvider.System);

        await sut.LinkAsync(ResourceType.SlackChannel, "C123", "DAVE-1");
        await sut.LinkAsync(ResourceType.SlackChannel, "C123", "DAVE-1");

        Assert.Equal(1, db.NewContext().ResourceMappings.Count());
    }

    [Fact]
    public async Task Linking_resource_to_different_work_item_conflicts()
    {
        using var db = new TestDb();
        await Seed(db, "DAVE-1", "DAVE-2");
        var sut = new MappingStore(db.Context, TimeProvider.System);

        await sut.LinkAsync(ResourceType.SlackChannel, "C123", "DAVE-1");

        await Assert.ThrowsAsync<MappingConflictException>(() =>
            sut.LinkAsync(ResourceType.SlackChannel, "C123", "DAVE-2"));
    }
}
