using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.Mapping;

/// <summary>Thrown when a resource is already linked to a different work item.</summary>
public class MappingConflictException : Exception
{
    public ResourceType ResourceType { get; }
    public string ResourceId { get; }
    public string ExistingWorkItemKey { get; }
    public string RequestedWorkItemKey { get; }

    public MappingConflictException(ResourceType type, string resourceId,
        string existingWorkItemKey, string requestedWorkItemKey)
        : base($"{type} '{resourceId}' is already linked to '{existingWorkItemKey}', " +
               $"cannot link to '{requestedWorkItemKey}'.")
    {
        ResourceType = type;
        ResourceId = resourceId;
        ExistingWorkItemKey = existingWorkItemKey;
        RequestedWorkItemKey = requestedWorkItemKey;
    }
}
