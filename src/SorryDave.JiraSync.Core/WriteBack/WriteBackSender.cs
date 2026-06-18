using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.Persistence;

namespace SorryDave.JiraSync.Core.WriteBack;

/// <summary>
/// Drains the write-back outbox to Jira. Posts a comment for a new record (storing the
/// returned comment id) and edits in place for an already-sent record, so redelivery never
/// duplicates content. Transient failures back off and retry; permanent failures are flagged.
/// </summary>
public class WriteBackSender
{
    private readonly JiraSyncDbContext _db;
    private readonly IJiraClient _jira;
    private readonly TimeProvider _clock;
    private readonly SyncOptions _options;
    private readonly ILogger<WriteBackSender> _logger;

    public WriteBackSender(
        JiraSyncDbContext db,
        IJiraClient jira,
        TimeProvider clock,
        IOptions<SyncOptions> options,
        ILogger<WriteBackSender> logger)
    {
        _db = db;
        _jira = jira;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Process all currently-due records. Returns the number successfully sent.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        // Filter by status in SQL (translatable), then apply the DateTimeOffset window in
        // memory — the SQLite provider cannot translate DateTimeOffset comparisons/ordering.
        var candidates = await _db.WriteBackRecords
            .Where(r => r.Status == WriteBackStatus.Pending || r.Status == WriteBackStatus.Retrying)
            .ToListAsync(ct);
        var due = candidates
            .Where(r => r.NextAttemptUtc <= now)
            .OrderBy(r => r.NextAttemptUtc)
            .ToList();

        var sent = 0;
        foreach (var record in due)
            if (await TrySendAsync(record, ct))
                sent++;

        return sent;
    }

    private async Task<bool> TrySendAsync(WriteBackRecord record, CancellationToken ct)
    {
        var body = RecordMarker.BuildCommentBody(record);
        record.Attempts++;

        try
        {
            if (string.IsNullOrEmpty(record.JiraCommentId))
                record.JiraCommentId = await _jira.AddCommentAsync(record.WorkItemKey, body, ct);
            else
                await _jira.UpdateCommentAsync(record.WorkItemKey, record.JiraCommentId, body, ct);

            record.Status = WriteBackStatus.Sent;
            record.LastError = null;
            record.UpdatedUtc = _clock.GetUtcNow();
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (JiraApiException ex)
        {
            await HandleFailure(record, ex, ex.IsTransient, ct);
            return false;
        }
        catch (Exception ex)
        {
            // Unknown errors are treated as transient so we don't drop the record.
            await HandleFailure(record, ex, isTransient: true, ct);
            return false;
        }
    }

    private async Task HandleFailure(WriteBackRecord record, Exception ex, bool isTransient, CancellationToken ct)
    {
        record.LastError = ex.Message;
        record.UpdatedUtc = _clock.GetUtcNow();

        if (!isTransient || record.Attempts >= _options.MaxWriteBackAttempts)
        {
            record.Status = WriteBackStatus.Failed;
            _logger.LogError(ex,
                "Write-back {Identity} for {Key} permanently failed after {Attempts} attempt(s).",
                record.RecordIdentity, record.WorkItemKey, record.Attempts);
        }
        else
        {
            record.Status = WriteBackStatus.Retrying;
            // Exponential backoff capped at 5 minutes.
            var seconds = Math.Min(300, Math.Pow(2, record.Attempts));
            record.NextAttemptUtc = _clock.GetUtcNow().AddSeconds(seconds);
            _logger.LogWarning(
                "Write-back {Identity} for {Key} failed (attempt {Attempts}); retrying after {Seconds}s.",
                record.RecordIdentity, record.WorkItemKey, record.Attempts, seconds);
        }

        await _db.SaveChangesAsync(ct);
    }
}
