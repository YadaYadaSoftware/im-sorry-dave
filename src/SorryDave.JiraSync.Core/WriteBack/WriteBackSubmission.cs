using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.WriteBack;

/// <summary>
/// A request to record a decision/answer/summary against a work item. <see cref="RecordIdentity"/>
/// is the caller's stable idempotency key for this logical record.
/// </summary>
public record WriteBackSubmission
{
    public required string WorkItemKey { get; init; }
    public required string RecordIdentity { get; init; }
    public required WriteBackKind Kind { get; init; }
    public required string Content { get; init; }
    public string? SourceUrl { get; init; }
    public string? Author { get; init; }
}
