namespace SorryDave.JiraSync.SmokeTui.Api;

/// <summary>Work item as returned by the API's <c>/workitems</c> endpoint.</summary>
public record WorkItemDto(
    string Key,
    string? ProjectKey,
    string? IssueType,
    string? Status,
    string? AssigneeDisplayName,
    string? Summary,
    bool IsDeleted);

/// <summary>Write-back outbox record as returned by the API.</summary>
public record WriteBackDto(
    Guid Id,
    string WorkItemKey,
    string RecordIdentity,
    string? Kind,
    string? Status,
    string? JiraCommentId,
    int Attempts,
    string? LastError);

/// <summary>A comment recorded by the fake Jira client (fake mode only).</summary>
public record CommentDto(string CommentId, string IssueKey, string Body);

/// <summary>Body for <c>POST /workitems/{key}/writeback</c>. Kind is the enum name, e.g. "Decision".</summary>
public record WriteBackRequest(string RecordIdentity, string Kind, string Content, string? SourceUrl, string? Author);

/// <summary>Result of a Slack channel command (provision/archive/unarchive).</summary>
public record SlackResultDto(string Outcome, string? ChannelId, string? ChannelName, string? Detail);
