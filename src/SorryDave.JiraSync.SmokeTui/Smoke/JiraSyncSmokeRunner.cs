using SorryDave.JiraSync.SmokeTui.Api;

namespace SorryDave.JiraSync.SmokeTui.Smoke;

/// <summary>
/// UI-independent happy-path smoke run, driven entirely through the API:
/// backfill → pick a work item → submit a write-back → drain the outbox → verify delivery.
/// </summary>
public class JiraSyncSmokeRunner
{
    private readonly IApiClient _api;

    public JiraSyncSmokeRunner(IApiClient api) => _api = api;

    public async Task<IReadOnlyList<SmokeStep>> RunAsync(CancellationToken ct = default)
    {
        var steps = new List<SmokeStep>();
        var identity = $"smoke-{Guid.NewGuid():N}";

        try
        {
            var mirrored = await _api.BackfillAsync(ct);
            steps.Add(new SmokeStep("Backfill work items", mirrored > 0, $"Mirrored {mirrored} issue(s)."));

            var items = await _api.GetWorkItemsAsync(ct);
            var target = items.FirstOrDefault(i => !i.IsDeleted);
            if (target is null)
            {
                steps.Add(new SmokeStep("Find a work item", false, "No work items available after backfill."));
                return steps;
            }
            steps.Add(new SmokeStep("Find a work item", true, $"Using {target.Key}."));

            await _api.SubmitWriteBackAsync(target.Key,
                new WriteBackRequest(identity, "Decision", "Smoke test: round-trip decision.", "smoke://run", "SmokeTui"), ct);
            steps.Add(new SmokeStep("Queue write-back", true, $"Queued {identity}."));

            var sent = await _api.DrainWriteBackAsync(ct);
            steps.Add(new SmokeStep("Deliver write-back", sent >= 1, $"Delivered {sent} record(s)."));

            var records = await _api.GetWriteBacksAsync(ct);
            var record = records.FirstOrDefault(r => r.RecordIdentity == identity);
            var ok = record is not null
                     && string.Equals(record.Status, "Sent", StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrEmpty(record.JiraCommentId);
            steps.Add(new SmokeStep("Verify delivery", ok,
                ok ? $"Status=Sent, comment {record!.JiraCommentId}."
                   : $"Status={record?.Status ?? "missing"}."));
        }
        catch (Exception ex)
        {
            steps.Add(new SmokeStep("Unexpected error", false, ex.Message));
        }

        return steps;
    }

    public static bool AllPassed(IReadOnlyList<SmokeStep> steps) => steps.Count > 0 && steps.All(s => s.Passed);
}
