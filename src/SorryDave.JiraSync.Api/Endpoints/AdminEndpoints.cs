using SorryDave.JiraSync.Core.Sync;
using SorryDave.JiraSync.Core.WriteBack;

namespace SorryDave.JiraSync.Api.Endpoints;

/// <summary>
/// On-demand admin operations. Backfill/reconcile/outbox normally run on background timers;
/// these endpoints let a client (e.g. the smoke-test console) drive them immediately.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/backfill", async (ReconciliationRunner runner, CancellationToken ct) =>
        {
            var mirrored = await runner.BackfillAsync(ct);
            return Results.Ok(new { mirrored });
        })
        .WithSummary("Run a full backfill of tracked work items now.");

        app.MapPost("/admin/reconcile", async (ReconciliationRunner runner, CancellationToken ct) =>
        {
            // Reconcile a recent window; the sweep is idempotent.
            var since = DateTimeOffset.UtcNow.AddHours(-1);
            var refreshed = await runner.SweepAsync(since, ct);
            return Results.Ok(new { refreshed });
        })
        .WithSummary("Run a reconciliation sweep over the last hour now.");

        app.MapPost("/admin/drain-writeback", async (WriteBackSender sender, CancellationToken ct) =>
        {
            var sent = await sender.ProcessDueAsync(ct);
            return Results.Ok(new { sent });
        })
        .WithSummary("Drain the write-back outbox immediately.");
    }
}
