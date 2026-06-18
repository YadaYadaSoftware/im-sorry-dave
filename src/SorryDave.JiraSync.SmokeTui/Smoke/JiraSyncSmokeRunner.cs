using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Persistence;
using SorryDave.JiraSync.Core.Sync;
using SorryDave.JiraSync.Core.WriteBack;

namespace SorryDave.JiraSync.SmokeTui.Smoke;

/// <summary>
/// UI-independent happy-path smoke run for jira-sync-core:
/// backfill → pick a work item → queue a write-back → drain the outbox → verify delivery.
/// Each step records pass/fail and a detail line. Kept free of Terminal.Gui so it is unit-testable.
/// </summary>
public class JiraSyncSmokeRunner
{
    private readonly IServiceProvider _root;

    public JiraSyncSmokeRunner(IServiceProvider root) => _root = root;

    public async Task<IReadOnlyList<SmokeStep>> RunAsync(CancellationToken ct = default)
    {
        var steps = new List<SmokeStep>();
        var identity = $"smoke-{Guid.NewGuid():N}";

        try
        {
            // 1. Backfill the tracked work items.
            int mirrored;
            using (var scope = _root.CreateScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<ReconciliationRunner>();
                mirrored = await runner.BackfillAsync(ct);
            }
            steps.Add(new SmokeStep("Backfill work items", mirrored > 0, $"Mirrored {mirrored} issue(s)."));

            // 2. Pick a work item to act on.
            string? key;
            using (var scope = _root.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<JiraSyncDbContext>();
                key = await db.WorkItems.Where(w => !w.IsDeleted)
                    .OrderBy(w => w.Key).Select(w => w.Key).FirstOrDefaultAsync(ct);
            }
            if (key is null)
            {
                steps.Add(new SmokeStep("Find a work item", false, "No work items available after backfill."));
                return steps;
            }
            steps.Add(new SmokeStep("Find a work item", true, $"Using {key}."));

            // 3. Queue a write-back.
            using (var scope = _root.CreateScope())
            {
                var writeBack = scope.ServiceProvider.GetRequiredService<IWriteBackService>();
                await writeBack.SubmitAsync(new WriteBackSubmission
                {
                    WorkItemKey = key,
                    RecordIdentity = identity,
                    Kind = WriteBackKind.Decision,
                    Content = "Smoke test: round-trip decision.",
                    SourceUrl = "smoke://run",
                    Author = "SmokeTui"
                }, ct);
            }
            steps.Add(new SmokeStep("Queue write-back", true, $"Queued {identity}."));

            // 4. Drain the outbox.
            int sent;
            using (var scope = _root.CreateScope())
            {
                var sender = scope.ServiceProvider.GetRequiredService<WriteBackSender>();
                sent = await sender.ProcessDueAsync(ct);
            }
            steps.Add(new SmokeStep("Deliver write-back", sent >= 1, $"Delivered {sent} record(s)."));

            // 5. Verify the record reached Sent with a comment id.
            using (var scope = _root.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<JiraSyncDbContext>();
                var record = await db.WriteBackRecords.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.RecordIdentity == identity, ct);
                var ok = record is { Status: WriteBackStatus.Sent } && !string.IsNullOrEmpty(record.JiraCommentId);
                steps.Add(new SmokeStep("Verify delivery", ok,
                    ok ? $"Status=Sent, comment {record!.JiraCommentId}."
                       : $"Status={record?.Status.ToString() ?? "missing"}."));
            }
        }
        catch (Exception ex)
        {
            steps.Add(new SmokeStep("Unexpected error", false, ex.Message));
        }

        return steps;
    }

    public static bool AllPassed(IReadOnlyList<SmokeStep> steps) => steps.Count > 0 && steps.All(s => s.Passed);
}
