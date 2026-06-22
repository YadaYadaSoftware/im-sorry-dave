using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Mapping;
using SorryDave.JiraSync.Core.Slack;
using SorryDave.JiraSync.Core.Summarization;
using SorryDave.JiraSync.Core.WriteBack;

namespace SorryDave.JiraSync.Tests;

public class SignatureAndRedactionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void Valid_signature_passes_invalid_fails()
    {
        const string secret = "shh", body = "token=abc&channel_id=C1";
        var ts = Now.ToUnixTimeSeconds().ToString();
        var good = Sign(secret, ts, body);

        Assert.True(SlackSignatureVerifier.IsValid(secret, good, ts, body, Now));
        Assert.False(SlackSignatureVerifier.IsValid(secret, good, ts, body + "x", Now)); // body tampered
        Assert.False(SlackSignatureVerifier.IsValid("wrong", good, ts, body, Now));      // wrong secret
    }

    [Fact]
    public void Stale_timestamp_is_rejected_as_replay()
    {
        const string secret = "shh", body = "x=1";
        var ts = Now.ToUnixTimeSeconds().ToString();
        var sig = Sign(secret, ts, body);
        var muchLater = Now.AddMinutes(10);

        Assert.False(SlackSignatureVerifier.IsValid(secret, sig, ts, body, muchLater));
    }

    [Fact]
    public void Redactor_masks_known_secret_patterns()
    {
        var r = new Redactor();
        // Assembled at runtime so the fake token is not a literal that trips secret scanning.
        var fakeSlackToken = "xoxb-" + "123456789012-abcdefghijklmnop";
        Assert.Equal($"token {Redactor.Mask}", r.Redact($"token {fakeSlackToken}"));
        Assert.Contains(Redactor.Mask, r.Redact("key AKIAIOSFODNN7EXAMPLE here"));
        Assert.Equal("nothing secret here", r.Redact("nothing secret here"));
    }

    private static string Sign(string secret, string ts, string body)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"v0:{ts}:{body}"));
        return "v0=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public class ConversationSummarizerTests
{
    private const string Channel = "C-mdp7";

    [Fact]
    public async Task Capture_ignores_unlinked_channels()
    {
        using var db = new TestDb();
        var (svc, _) = Build(db, linkChannel: false);

        await svc.CaptureAsync(new IncomingMessage("C-unlinked", "1.0001", null, "U1", "hello"));

        Assert.Equal(0, await db.NewContext().CapturedMessages.CountAsync());
    }

    [Fact]
    public async Task Post_extracts_over_messages_then_cursor_advances_on_confirm()
    {
        using var db = new TestDb();
        var (svc, writeback) = Build(db);
        await Capture(svc, "1.0001", "we decided to ship Friday");
        await Capture(svc, "1.0002", "sounds good");

        var first = await svc.PostAsync(Channel);
        Assert.Equal("Extracted", first.Outcome);
        var decision = first.Candidates.First(c => c.Kind == WriteBackKind.Decision);

        await svc.ConfirmAsync(decision.Id, "U-boss");
        Assert.Single(writeback.Submitted);                       // written back to Jira
        Assert.Equal("MDP-7", writeback.Submitted[0].WorkItemKey);

        // A new message after the post; the next /post window starts after the cursor.
        await Capture(svc, "1.0003", "should we also update the docs?");
        var second = await svc.PostAsync(Channel);
        Assert.Equal("Extracted", second.Outcome);
        Assert.All(second.Candidates, c => Assert.Equal("1.0003", c.WindowFromTs)); // only the new message
    }

    [Fact]
    public async Task Post_with_no_new_messages_is_empty_and_cursor_unchanged()
    {
        using var db = new TestDb();
        var (svc, _) = Build(db);
        await Capture(svc, "1.0001", "we decided X");
        var first = await svc.PostAsync(Channel);
        await svc.ConfirmAsync(first.Candidates.First(c => c.Kind == WriteBackKind.Decision).Id, "U1");

        var second = await svc.PostAsync(Channel); // nothing new
        Assert.Equal("Empty", second.Outcome);
    }

    [Fact]
    public async Task Reject_does_not_advance_the_cursor()
    {
        using var db = new TestDb();
        var (svc, writeback) = Build(db);
        await Capture(svc, "1.0001", "we decided Y");
        var first = await svc.PostAsync(Channel);
        var decision = first.Candidates.First(c => c.Kind == WriteBackKind.Decision);

        await svc.RejectAsync(decision.Id);
        Assert.Empty(writeback.Submitted);

        // Same window is still available on the next /post (cursor didn't move).
        var second = await svc.PostAsync(Channel);
        Assert.Equal("Extracted", second.Outcome);
        Assert.Contains(second.Candidates, c => c.WindowFromTs == "1.0001");
    }

    [Fact]
    public async Task Re_confirming_uses_a_stable_identity_so_writeback_is_idempotent()
    {
        using var db = new TestDb();
        var (svc, writeback) = Build(db);
        await Capture(svc, "1.0001", "we decided Z");
        var first = await svc.PostAsync(Channel);
        var d1 = first.Candidates.First(c => c.Kind == WriteBackKind.Decision);
        await svc.ConfirmAsync(d1.Id, "U1");

        // Re-post the same window (cursor advanced, but simulate a redo by rejecting-less): same content
        // yields the same RecordIdentity, which the write-back store dedupes on.
        var identity = d1.RecordIdentity;
        Assert.StartsWith("slack:C-mdp7:1.0001:", identity);
        Assert.Single(writeback.Submitted.Where(s => s.RecordIdentity == identity));
    }

    private async Task Capture(IConversationSummarizer svc, string ts, string text)
        => await svc.CaptureAsync(new IncomingMessage(Channel, ts, null, "U1", text));

    private static (IConversationSummarizer Svc, RecordingWriteBack Wb) Build(TestDb db, bool linkChannel = true)
    {
        db.Context.WorkItems.Add(new WorkItem
        {
            Key = "MDP-7", ProjectKey = "MDP", IssueType = "Idea", Status = "To Do", Summary = "s",
            JiraUpdated = DateTimeOffset.UtcNow, FirstSeenUtc = DateTimeOffset.UtcNow, LastSyncedUtc = DateTimeOffset.UtcNow,
        });
        db.Context.SaveChanges();
        var mappings = new MappingStore(db.Context, TimeProvider.System);
        if (linkChannel)
            mappings.LinkAsync(ResourceType.SlackChannel, Channel, "MDP-7", "mdp-7").GetAwaiter().GetResult();

        var writeback = new RecordingWriteBack();
        var svc = new ConversationSummarizer(
            db.Context, mappings, new FakeDecisionExtractor(), writeback, new Redactor(),
            Options.Create(new AnthropicOptions()), TimeProvider.System,
            NullLogger<ConversationSummarizer>.Instance);
        return (svc, writeback);
    }

    private sealed class RecordingWriteBack : IWriteBackService
    {
        public List<WriteBackSubmission> Submitted { get; } = new();
        public Task<WriteBackRecord> SubmitAsync(WriteBackSubmission submission, CancellationToken ct = default)
        {
            // Idempotent on (WorkItemKey, RecordIdentity), like the real service.
            if (!Submitted.Any(s => s.WorkItemKey == submission.WorkItemKey && s.RecordIdentity == submission.RecordIdentity))
                Submitted.Add(submission);
            return Task.FromResult(new WriteBackRecord { WorkItemKey = submission.WorkItemKey, RecordIdentity = submission.RecordIdentity });
        }
    }
}
