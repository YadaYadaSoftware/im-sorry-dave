using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Persistence;

namespace SorryDave.JiraSync.Core.WriteBack;

public class WriteBackService : IWriteBackService
{
    private readonly JiraSyncDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<WriteBackService> _logger;

    public WriteBackService(JiraSyncDbContext db, TimeProvider clock, ILogger<WriteBackService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<WriteBackRecord> SubmitAsync(WriteBackSubmission submission, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(submission.WorkItemKey))
            throw new ArgumentException("WorkItemKey is required.", nameof(submission));
        if (string.IsNullOrWhiteSpace(submission.RecordIdentity))
            throw new ArgumentException("RecordIdentity is required.", nameof(submission));

        var now = _clock.GetUtcNow();
        var existing = await _db.WriteBackRecords.FirstOrDefaultAsync(
            r => r.WorkItemKey == submission.WorkItemKey && r.RecordIdentity == submission.RecordIdentity, ct);

        if (existing is null)
        {
            var record = new WriteBackRecord
            {
                Id = Guid.NewGuid(),
                WorkItemKey = submission.WorkItemKey,
                RecordIdentity = submission.RecordIdentity,
                Kind = submission.Kind,
                Content = submission.Content,
                SourceUrl = submission.SourceUrl,
                Author = submission.Author,
                Status = WriteBackStatus.Pending,
                CreatedUtc = now,
                UpdatedUtc = now,
                NextAttemptUtc = now
            };
            _db.WriteBackRecords.Add(record);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Queued write-back {Identity} for {Key}.",
                record.RecordIdentity, record.WorkItemKey);
            return record;
        }

        var unchanged =
            existing.Content == submission.Content &&
            existing.Kind == submission.Kind &&
            existing.SourceUrl == submission.SourceUrl &&
            existing.Author == submission.Author;

        if (unchanged)
        {
            // Idempotent: same logical record, no new Jira content.
            return existing;
        }

        // Edited record: update and re-queue an in-place Jira edit (keeps JiraCommentId).
        existing.Content = submission.Content;
        existing.Kind = submission.Kind;
        existing.SourceUrl = submission.SourceUrl;
        existing.Author = submission.Author;
        existing.Status = WriteBackStatus.Pending;
        existing.UpdatedUtc = now;
        existing.NextAttemptUtc = now;
        existing.LastError = null;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated existing write-back {Identity} for {Key}; re-queued edit.",
            existing.RecordIdentity, existing.WorkItemKey);
        return existing;
    }
}
