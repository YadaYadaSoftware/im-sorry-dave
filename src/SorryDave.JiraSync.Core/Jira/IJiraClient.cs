namespace SorryDave.JiraSync.Core.Jira;

/// <summary>A comment's @mention accountIds and its readable text (flattened from ADF).</summary>
public sealed record CommentContent(IReadOnlyList<string> MentionAccountIds, string? Text);

/// <summary>Abstraction over the Jira REST API. Implemented by the real client and an
/// in-memory fake used for local review and tests.</summary>
public interface IJiraClient
{
    /// <summary>Fetch a single issue, or null if it does not exist (404).</summary>
    Task<JiraIssueData?> GetIssueAsync(string key, CancellationToken ct = default);

    /// <summary>Run a JQL search and return the matching issues.</summary>
    Task<IReadOnlyList<JiraIssueData>> SearchAsync(string jql, CancellationToken ct = default);

    /// <summary>Add a comment to an issue and return the created comment id.</summary>
    Task<string> AddCommentAsync(string issueKey, string body, CancellationToken ct = default);

    /// <summary>Edit an existing comment in place.</summary>
    Task UpdateCommentAsync(string issueKey, string commentId, string body, CancellationToken ct = default);

    /// <summary>A comment's @mention accountIds and text from its ADF body (used to invite and welcome
    /// mentioned users, since webhook payloads may render the body without accountIds).</summary>
    Task<CommentContent> GetCommentAsync(string issueKey, string commentId, CancellationToken ct = default);
}
