using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.Persistence;

namespace SorryDave.JiraSync.Tests;

/// <summary>
/// A disposable in-memory SQLite database for tests. The connection is kept open so the
/// :memory: database survives for the lifetime of the harness.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public JiraSyncDbContext Context { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<JiraSyncDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new JiraSyncDbContext(options);
        Context.Database.EnsureCreated();
    }

    /// <summary>A fresh context over the same database (to assert without tracking artefacts).</summary>
    public JiraSyncDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<JiraSyncDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new JiraSyncDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}

/// <summary>IJiraClient stub whose behaviour is supplied per-test.</summary>
public sealed class StubJiraClient : IJiraClient
{
    public List<(string Issue, string Body)> Added { get; } = new();
    public List<(string Issue, string CommentId, string Body)> Updated { get; } = new();
    public Func<string, string>? OnAdd { get; set; }
    public Action? OnAny { get; set; }

    private int _seq;

    public Task<JiraIssueData?> GetIssueAsync(string key, CancellationToken ct = default)
        => Task.FromResult<JiraIssueData?>(null);

    public Task<IReadOnlyList<JiraIssueData>> SearchAsync(string jql, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JiraIssueData>>(Array.Empty<JiraIssueData>());

    public Task<string> AddCommentAsync(string issueKey, string body, CancellationToken ct = default)
    {
        OnAny?.Invoke();
        if (OnAdd is not null) return Task.FromResult(OnAdd(issueKey));
        Added.Add((issueKey, body));
        return Task.FromResult($"c{Interlocked.Increment(ref _seq)}");
    }

    public Task UpdateCommentAsync(string issueKey, string commentId, string body, CancellationToken ct = default)
    {
        OnAny?.Invoke();
        Updated.Add((issueKey, commentId, body));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetCommentMentionsAsync(string issueKey, string commentId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
