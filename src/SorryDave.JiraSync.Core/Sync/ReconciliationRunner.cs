using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.Persistence;

namespace SorryDave.JiraSync.Core.Sync;

/// <summary>
/// One unit of reconciliation work (scoped). A backfill mirrors every tracked issue and
/// detects deletions; an incremental sweep refreshes issues updated since the last run.
/// </summary>
public class ReconciliationRunner
{
    private readonly IJiraClient _jira;
    private readonly IWorkItemSyncService _sync;
    private readonly JiraSyncDbContext _db;
    private readonly JiraOptions _options;
    private readonly ILogger<ReconciliationRunner> _logger;

    public ReconciliationRunner(
        IJiraClient jira,
        IWorkItemSyncService sync,
        JiraSyncDbContext db,
        IOptions<JiraOptions> options,
        ILogger<ReconciliationRunner> logger)
    {
        _jira = jira;
        _sync = sync;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> BackfillAsync(CancellationToken ct = default)
    {
        var jql = BuildJql(updatedSince: null);
        var issues = await _jira.SearchAsync(jql, ct);
        foreach (var issue in issues)
            await _sync.ApplyIssueAsync(issue, ct);

        // Anything we still track that Jira did not return is treated as deleted.
        var seen = issues.Select(i => i.Key).ToHashSet();
        var stale = await _db.WorkItems
            .Where(w => !w.IsDeleted)
            .Select(w => w.Key)
            .ToListAsync(ct);
        foreach (var key in stale.Where(k => !seen.Contains(k)))
            await _sync.ApplyDeletionAsync(key, ct);

        _logger.LogInformation("Backfill complete: {Count} issues mirrored.", issues.Count);
        return issues.Count;
    }

    public async Task<int> SweepAsync(DateTimeOffset updatedSince, CancellationToken ct = default)
    {
        var jql = BuildJql(updatedSince);
        var issues = await _jira.SearchAsync(jql, ct);
        foreach (var issue in issues)
            await _sync.ApplyIssueAsync(issue, ct);

        if (issues.Count > 0)
            _logger.LogInformation("Reconciliation sweep refreshed {Count} issue(s).", issues.Count);
        return issues.Count;
    }

    private string BuildJql(DateTimeOffset? updatedSince)
    {
        var sb = new StringBuilder();
        if (_options.ProjectKeys.Count > 0)
            sb.Append("project in (").Append(string.Join(", ", _options.ProjectKeys)).Append(')');

        if (updatedSince is { } since)
        {
            if (sb.Length > 0) sb.Append(" AND ");
            // JQL accepts minute precision in the site timezone; overlap covers skew.
            sb.Append("updated >= \"")
              .Append(since.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
              .Append('"');
        }

        if (!string.IsNullOrWhiteSpace(_options.AdditionalJql))
        {
            if (sb.Length > 0) sb.Append(" AND ");
            sb.Append('(').Append(_options.AdditionalJql).Append(')');
        }

        if (sb.Length > 0) sb.Append(" ORDER BY updated ASC");
        return sb.ToString();
    }
}
