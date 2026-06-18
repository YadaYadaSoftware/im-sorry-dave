using Microsoft.Extensions.Logging.Abstractions;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.WriteBack;

namespace SorryDave.JiraSync.Tests;

public class WriteBackServiceTests
{
    private static WriteBackService NewService(TestDb db) =>
        new(db.Context, TimeProvider.System, NullLogger<WriteBackService>.Instance);

    private static WriteBackSubmission Submission(string content) => new()
    {
        WorkItemKey = "DAVE-1",
        RecordIdentity = "decision-42",
        Kind = WriteBackKind.Decision,
        Content = content,
        SourceUrl = "https://slack.example/thread/1",
        Author = "Dave"
    };

    [Fact]
    public async Task First_submission_queues_pending_record()
    {
        using var db = new TestDb();
        var sut = NewService(db);

        var record = await sut.SubmitAsync(Submission("We will open the doors."));

        Assert.Equal(WriteBackStatus.Pending, record.Status);
        Assert.Equal(1, db.NewContext().WriteBackRecords.Count());
    }

    [Fact]
    public async Task Resubmitting_identical_record_does_not_duplicate()
    {
        using var db = new TestDb();
        var sut = NewService(db);

        var first = await sut.SubmitAsync(Submission("We will open the doors."));
        var second = await sut.SubmitAsync(Submission("We will open the doors."));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, db.NewContext().WriteBackRecords.Count());
    }

    [Fact]
    public async Task Resubmitting_edited_record_updates_in_place_and_requeues()
    {
        using var db = new TestDb();
        var sut = NewService(db);

        var first = await sut.SubmitAsync(Submission("Draft decision."));
        // Simulate it having been delivered already.
        first.Status = WriteBackStatus.Sent;
        first.JiraCommentId = "c1";
        await db.Context.SaveChangesAsync();

        var edited = await sut.SubmitAsync(Submission("Final decision: open the doors."));

        Assert.Equal(first.Id, edited.Id);
        Assert.Equal(WriteBackStatus.Pending, edited.Status);
        Assert.Equal("c1", edited.JiraCommentId); // keeps the comment id for an in-place edit
        Assert.Equal("Final decision: open the doors.", edited.Content);
        Assert.Equal(1, db.NewContext().WriteBackRecords.Count());
    }
}
