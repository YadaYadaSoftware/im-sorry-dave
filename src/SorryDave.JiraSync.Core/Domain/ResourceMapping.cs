namespace SorryDave.JiraSync.Core.Domain;

/// <summary>
/// A unique association between an external resource and a work item. There is at most
/// one mapping per (<see cref="ResourceType"/>, <see cref="ResourceId"/>) pair.
/// </summary>
public class ResourceMapping
{
    public long Id { get; set; }

    public ResourceType ResourceType { get; set; }

    /// <summary>The external identifier, e.g. a Slack channel id or GitHub PR node id.</summary>
    public string ResourceId { get; set; } = default!;

    public string WorkItemKey { get; set; } = default!;
    public WorkItem? WorkItem { get; set; }

    /// <summary>Optional human-friendly label (channel name, PR title, change name).</summary>
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}
