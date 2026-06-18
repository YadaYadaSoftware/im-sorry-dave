using Microsoft.EntityFrameworkCore;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.Mapping;
using SorryDave.JiraSync.Core.Persistence;
using SorryDave.JiraSync.Core.WriteBack;

namespace SorryDave.JiraSync.Api.Endpoints;

public static class WorkItemEndpoints
{
    public record WriteBackRequest(string RecordIdentity, WriteBackKind Kind, string Content, string? SourceUrl, string? Author);
    public record LinkRequest(ResourceType ResourceType, string ResourceId, string WorkItemKey, string? DisplayName);

    public static void MapWorkItemEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Work items (read) ---
        app.MapGet("/workitems", async (JiraSyncDbContext db, bool? includeDeleted, CancellationToken ct) =>
        {
            var query = db.WorkItems.AsNoTracking();
            if (includeDeleted != true) query = query.Where(w => !w.IsDeleted);
            var items = await query.OrderBy(w => w.Key).ToListAsync(ct);
            return Results.Ok(items.Select(ToDto));
        })
        .WithSummary("List mirrored work items.");

        app.MapGet("/workitems/{key}", async (string key, JiraSyncDbContext db, CancellationToken ct) =>
        {
            var item = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Key == key, ct);
            return item is null ? Results.NotFound() : Results.Ok(ToDto(item));
        })
        .WithSummary("Get a single mirrored work item.");

        // --- Write-back ---
        app.MapPost("/workitems/{key}/writeback", async (
            string key, WriteBackRequest body, IWriteBackService writeBack, JiraSyncDbContext db, CancellationToken ct) =>
        {
            if (!await db.WorkItems.AnyAsync(w => w.Key == key, ct))
                return Results.NotFound(new { error = $"work item '{key}' is not tracked" });

            var record = await writeBack.SubmitAsync(new WriteBackSubmission
            {
                WorkItemKey = key,
                RecordIdentity = body.RecordIdentity,
                Kind = body.Kind,
                Content = body.Content,
                SourceUrl = body.SourceUrl,
                Author = body.Author
            }, ct);

            return Results.Accepted($"/writeback/{record.Id}", ToDto(record));
        })
        .WithSummary("Queue a decision/answer/summary for idempotent write-back to Jira.");

        app.MapGet("/writeback", async (JiraSyncDbContext db, CancellationToken ct) =>
        {
            // Order in memory: the SQLite provider cannot translate DateTimeOffset ordering.
            var records = await db.WriteBackRecords.AsNoTracking().ToListAsync(ct);
            return Results.Ok(records.OrderByDescending(r => r.UpdatedUtc).Select(ToDto));
        })
        .WithSummary("Inspect the write-back outbox and its delivery status.");

        // --- Mappings ---
        app.MapGet("/mappings", async (JiraSyncDbContext db, CancellationToken ct) =>
        {
            var mappings = await db.ResourceMappings.AsNoTracking().ToListAsync(ct);
            return Results.Ok(mappings);
        })
        .WithSummary("List resource ↔ work-item mappings.");

        app.MapPost("/mappings", async (LinkRequest body, IMappingStore store, CancellationToken ct) =>
        {
            try
            {
                var mapping = await store.LinkAsync(body.ResourceType, body.ResourceId, body.WorkItemKey, body.DisplayName, ct);
                return Results.Ok(mapping);
            }
            catch (MappingConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithSummary("Link an external resource to a work item (rejects conflicting links).");

        // --- Local-review aid: inspect the in-memory fake Jira's comments ---
        app.MapGet("/debug/jira-comments", (IJiraClient jira) =>
        {
            if (jira is not FakeJiraClient fake)
                return Results.NotFound(new { error = "real Jira client is in use; inspect comments in Jira" });

            return Results.Ok(fake.Comments.Select(kv => new
            {
                commentId = kv.Key,
                issueKey = kv.Value.IssueKey,
                body = kv.Value.Body
            }));
        })
        .WithSummary("View comments written by the in-memory fake Jira client (fake mode only).");
    }

    private static object ToDto(WorkItem w) => new
    {
        w.Key, w.ProjectKey, w.IssueType, w.Status,
        w.AssigneeDisplayName, w.ReporterDisplayName, w.Summary, w.Description,
        w.Labels, w.JiraUpdated, w.IsDeleted, w.FirstSeenUtc, w.LastSyncedUtc
    };

    private static object ToDto(WriteBackRecord r) => new
    {
        r.Id, r.WorkItemKey, r.RecordIdentity, Kind = r.Kind.ToString(), r.Content,
        r.SourceUrl, r.Author, Status = r.Status.ToString(), r.JiraCommentId,
        r.Attempts, r.LastError, r.CreatedUtc, r.UpdatedUtc, r.NextAttemptUtc
    };
}
