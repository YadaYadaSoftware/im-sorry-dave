using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.Mapping;

/// <summary>
/// The integration hub: associates external resources with work items and resolves in both
/// directions. Other capabilities (Slack/GitHub/OpenSpec) depend on this rather than each
/// keeping their own mapping.
/// </summary>
public interface IMappingStore
{
    /// <summary>
    /// Link a resource to a work item. Idempotent if the same link already exists; throws
    /// <see cref="MappingConflictException"/> if the resource is linked to a different work item.
    /// </summary>
    Task<ResourceMapping> LinkAsync(ResourceType type, string resourceId, string workItemKey,
        string? displayName = null, CancellationToken ct = default);

    /// <summary>Resolve the work item linked to a given resource, or null if none.</summary>
    Task<WorkItem?> ResolveByResourceAsync(ResourceType type, string resourceId, CancellationToken ct = default);

    /// <summary>All resources linked to a work item.</summary>
    Task<IReadOnlyList<ResourceMapping>> GetMappingsForWorkItemAsync(string workItemKey, CancellationToken ct = default);
}
