using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.WriteBack;

namespace SorryDave.JiraSync.Tests;

public class WriteBackSenderTests
{
    private static WriteBackSender NewSender(TestDb db, IJiraClient jira, int maxAttempts = 8) =>
        new(db.Context, jira, TimeProvider.System,
            Options.Create(new SyncOptions { MaxWriteBackAttempts = maxAttempts }),
            NullLogger<WriteBackSender>.Instance);

    private static async Task<WriteBackRecord> QueueRecord(TestDb db, string? commentId = null)
    {
        var record = new WriteBackRecord
        {
            Id = Guid.NewGuid(),
            WorkItemKey = "DAVE-1",
            RecordIdentity = "decision-1",
            Kind = WriteBackKind.Decision,
            Content = "Open the doors.",
            Status = WriteBackStatus.Pending,
            JiraCommentId = commentId,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
            NextAttemptUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        db.Context.WriteBackRecords.Add(record);
        await db.Context.SaveChangesAsync();
        return record;
    }

    [Fact]
    public async Task Pending_record_is_posted_and_marked_sent()
    {
        using var db = new TestDb();
        var jira = new StubJiraClient();
        await QueueRecord(db);

        var sent = await NewSender(db, jira).ProcessDueAsync();

        Assert.Equal(1, sent);
        Assert.Single(jira.Added);
        var stored = db.NewContext().WriteBackRecords.Single();
        Assert.Equal(WriteBackStatus.Sent, stored.Status);
        Assert.False(string.IsNullOrEmpty(stored.JiraCommentId));
    }

    [Fact]
    public async Task Already_sent_record_is_edited_in_place_not_duplicated()
    {
        using var db = new TestDb();
        var jira = new StubJiraClient();
        await QueueRecord(db, commentId: "existing-comment");

        await NewSender(db, jira).ProcessDueAsync();

        Assert.Empty(jira.Added);
        Assert.Single(jira.Updated);
        Assert.Equal("existing-comment", jira.Updated[0].CommentId);
    }

    [Fact]
    public async Task Permanent_failure_marks_record_failed()
    {
        using var db = new TestDb();
        var jira = new StubJiraClient
        {
            OnAdd = _ => throw new JiraApiException("not found", HttpStatusCode.NotFound, isTransient: false)
        };
        await QueueRecord(db);

        await NewSender(db, jira).ProcessDueAsync();

        Assert.Equal(WriteBackStatus.Failed, db.NewContext().WriteBackRecords.Single().Status);
    }

    [Fact]
    public async Task Transient_failure_schedules_retry()
    {
        using var db = new TestDb();
        var jira = new StubJiraClient
        {
            OnAdd = _ => throw new JiraApiException("rate limited", HttpStatusCode.TooManyRequests, isTransient: true)
        };
        await QueueRecord(db);

        await NewSender(db, jira).ProcessDueAsync();

        var stored = db.NewContext().WriteBackRecords.Single();
        Assert.Equal(WriteBackStatus.Retrying, stored.Status);
        Assert.True(stored.NextAttemptUtc > DateTimeOffset.UtcNow);
        Assert.Equal(1, stored.Attempts);
    }

    [Fact]
    public async Task Transient_failure_becomes_failed_after_max_attempts()
    {
        using var db = new TestDb();
        var jira = new StubJiraClient
        {
            OnAdd = _ => throw new JiraApiException("server error", HttpStatusCode.InternalServerError, isTransient: true)
        };
        var record = await QueueRecord(db);
        record.Attempts = 7; // one below the max of 8
        await db.Context.SaveChangesAsync();

        await NewSender(db, jira, maxAttempts: 8).ProcessDueAsync();

        Assert.Equal(WriteBackStatus.Failed, db.NewContext().WriteBackRecords.Single().Status);
    }
}
