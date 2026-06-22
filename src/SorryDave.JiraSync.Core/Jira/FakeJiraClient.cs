using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SorryDave.JiraSync.Core.Jira;

/// <summary>
/// In-memory <see cref="IJiraClient"/> used when no Jira credentials are configured. It is
/// seeded with a couple of sample issues so the service is fully reviewable locally:
/// backfill/reconciliation populate work items, and write-back "comments" are recorded here
/// and exposed for inspection. This is NOT for production.
/// </summary>
public class FakeJiraClient : IJiraClient
{
    private readonly ILogger<FakeJiraClient> _logger;
    private readonly ConcurrentDictionary<string, JiraIssueData> _issues = new();
    private readonly ConcurrentDictionary<string, (string IssueKey, string Body)> _comments = new();
    private int _commentSeq;

    public FakeJiraClient(ILogger<FakeJiraClient> logger)
    {
        _logger = logger;
        Seed();
    }

    /// <summary>Comments recorded by write-back, for the review/debug endpoint.</summary>
    public IReadOnlyDictionary<string, (string IssueKey, string Body)> Comments => _comments;

    public Task<JiraIssueData?> GetIssueAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_issues.TryGetValue(key, out var issue) ? issue : null);

    public Task<IReadOnlyList<JiraIssueData>> SearchAsync(string jql, CancellationToken ct = default)
    {
        _logger.LogInformation("FakeJiraClient.Search ignoring JQL '{Jql}' and returning all {Count} seeded issues.",
            jql, _issues.Count);
        return Task.FromResult<IReadOnlyList<JiraIssueData>>(_issues.Values.ToList());
    }

    public Task<string> AddCommentAsync(string issueKey, string body, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _commentSeq).ToString();
        _comments[id] = (issueKey, body);
        _logger.LogInformation("FakeJiraClient: added comment {Id} to {Issue}:\n{Body}", id, issueKey, body);
        return Task.FromResult(id);
    }

    public Task UpdateCommentAsync(string issueKey, string commentId, string body, CancellationToken ct = default)
    {
        _comments[commentId] = (issueKey, body);
        _logger.LogInformation("FakeJiraClient: updated comment {Id} on {Issue}.", commentId, issueKey);
        return Task.CompletedTask;
    }

    public Task<CommentContent> GetCommentAsync(string issueKey, string commentId, CancellationToken ct = default)
        => Task.FromResult(new CommentContent(Array.Empty<string>(), null));

    private void Seed()
    {
        var now = DateTimeOffset.UtcNow.AddHours(-1);
        _issues["DAVE-1"] = new JiraIssueData
        {
            Key = "DAVE-1", ProjectKey = "DAVE", IssueType = "Story", Status = "To Do",
            AssigneeDisplayName = "Dave Bowman", ReporterDisplayName = "HAL 9000",
            Summary = "Open the pod bay doors", Description = "Allow the pod bay doors to be opened on request.",
            Labels = new() { "mission-critical" }, Updated = now
        };
        _issues["DAVE-2"] = new JiraIssueData
        {
            Key = "DAVE-2", ProjectKey = "DAVE", IssueType = "Bug", Status = "In Progress",
            AssigneeDisplayName = "Frank Poole", ReporterDisplayName = "Dave Bowman",
            Summary = "AE-35 unit predicted failure", Description = "Investigate the predicted AE-35 antenna fault.",
            Labels = new() { "hardware" }, Updated = now
        };
    }
}
