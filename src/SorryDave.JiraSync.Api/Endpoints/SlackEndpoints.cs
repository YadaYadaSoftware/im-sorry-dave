using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Mapping;
using SorryDave.JiraSync.Core.Slack;

namespace SorryDave.JiraSync.Api.Endpoints;

/// <summary>
/// Slack channel commands over the work item: provision/archive/unarchive (via the lifecycle
/// service), link an existing channel, and show the linked channel. All mutating endpoints honor
/// <c>?dryRun=true</c>.
/// </summary>
public static class SlackEndpoints
{
    public static void MapSlackEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/slack");

        group.MapPost("/{key}/provision", async (string key, bool? dryRun, ISlackChannelService svc, CancellationToken ct)
            => Results.Ok(await svc.ProvisionAsync(key, dryRun ?? false, ct)));

        group.MapPost("/{key}/archive", async (string key, bool? dryRun, ISlackChannelService svc, CancellationToken ct)
            => Results.Ok(await svc.ArchiveAsync(key, dryRun ?? false, ct)));

        group.MapPost("/{key}/unarchive", async (string key, bool? dryRun, ISlackChannelService svc, CancellationToken ct)
            => Results.Ok(await svc.UnarchiveAsync(key, dryRun ?? false, ct)));

        group.MapPost("/reconcile", async (ISlackChannelService svc, CancellationToken ct)
            => Results.Ok(new { drift = await svc.ReconcileLinksAsync(ct) }));

        group.MapGet("/{key}/channel", async (string key, IMappingStore mappings, CancellationToken ct) =>
        {
            var mapping = (await mappings.GetMappingsForWorkItemAsync(key, ct))
                .FirstOrDefault(m => m.ResourceType == ResourceType.SlackChannel);
            return mapping is null
                ? Results.Ok(new { workItemKey = key, channelId = (string?)null, channelName = (string?)null })
                : Results.Ok(new { workItemKey = key, channelId = mapping.ResourceId, channelName = mapping.DisplayName });
        });

        group.MapPost("/{key}/link", async (string key, string channelId, string? channelName, bool? dryRun, IMappingStore mappings, CancellationToken ct) =>
        {
            if (dryRun ?? false)
                return Results.Ok(new { workItemKey = key, channelId, channelName, outcome = "DryRun" });
            try
            {
                var mapping = await mappings.LinkAsync(ResourceType.SlackChannel, channelId, key, channelName, ct);
                return Results.Ok(new { workItemKey = mapping.WorkItemKey, channelId = mapping.ResourceId, channelName = mapping.DisplayName, outcome = "Linked" });
            }
            catch (MappingConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });
    }
}
