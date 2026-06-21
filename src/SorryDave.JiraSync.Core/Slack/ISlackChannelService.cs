using SorryDave.JiraSync.Core.Sync;

namespace SorryDave.JiraSync.Core.Slack;

/// <summary>Outcome of a channel operation, suitable for console/API reporting.</summary>
public sealed record ChannelProvisionResult(
    string Outcome,
    string? ChannelId = null,
    string? ChannelName = null,
    string? Detail = null);

/// <summary>
/// Provisions and manages a Slack channel per work item. Also an <see cref="IWorkItemChangeListener"/>
/// so Jira status/assignee changes drive lifecycle (archive/unarchive) and context reflection.
/// </summary>
public interface ISlackChannelService : IWorkItemChangeListener
{
    /// <summary>Create (or report the existing) channel for an eligible work item and link it.</summary>
    Task<ChannelProvisionResult> ProvisionAsync(string workItemKey, bool dryRun = false, CancellationToken ct = default);

    /// <summary>Archive the work item's linked channel (posts a closing note first).</summary>
    Task<ChannelProvisionResult> ArchiveAsync(string workItemKey, bool dryRun = false, CancellationToken ct = default);

    /// <summary>Unarchive the work item's linked channel (posts a re-activation note).</summary>
    Task<ChannelProvisionResult> UnarchiveAsync(string workItemKey, bool dryRun = false, CancellationToken ct = default);
}
