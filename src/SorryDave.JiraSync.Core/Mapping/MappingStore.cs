using Microsoft.EntityFrameworkCore;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Persistence;

namespace SorryDave.JiraSync.Core.Mapping;

public class MappingStore : IMappingStore
{
    private readonly JiraSyncDbContext _db;
    private readonly TimeProvider _clock;

    public MappingStore(JiraSyncDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ResourceMapping> LinkAsync(ResourceType type, string resourceId, string workItemKey,
        string? displayName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            throw new ArgumentException("Resource id is required.", nameof(resourceId));

        var existing = await _db.ResourceMappings
            .FirstOrDefaultAsync(m => m.ResourceType == type && m.ResourceId == resourceId, ct);

        if (existing is not null)
        {
            if (existing.WorkItemKey != workItemKey)
                throw new MappingConflictException(type, resourceId, existing.WorkItemKey, workItemKey);

            // Same link already present — idempotent; refresh display name if provided.
            if (displayName is not null && existing.DisplayName != displayName)
            {
                existing.DisplayName = displayName;
                await _db.SaveChangesAsync(ct);
            }
            return existing;
        }

        var mapping = new ResourceMapping
        {
            ResourceType = type,
            ResourceId = resourceId,
            WorkItemKey = workItemKey,
            DisplayName = displayName,
            CreatedUtc = _clock.GetUtcNow()
        };
        _db.ResourceMappings.Add(mapping);
        await _db.SaveChangesAsync(ct);
        return mapping;
    }

    public Task<WorkItem?> ResolveByResourceAsync(ResourceType type, string resourceId, CancellationToken ct = default)
        => _db.ResourceMappings
            .Where(m => m.ResourceType == type && m.ResourceId == resourceId)
            .Select(m => m.WorkItem)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ResourceMapping>> GetMappingsForWorkItemAsync(string workItemKey, CancellationToken ct = default)
        => await _db.ResourceMappings
            .Where(m => m.WorkItemKey == workItemKey)
            .ToListAsync(ct);
}
