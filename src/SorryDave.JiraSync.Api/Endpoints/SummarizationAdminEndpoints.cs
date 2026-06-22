using Microsoft.EntityFrameworkCore;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Persistence;
using SorryDave.JiraSync.Core.Summarization;

namespace SorryDave.JiraSync.Api.Endpoints;

/// <summary>
/// Slack-free admin endpoints for smoke-testing the summarization pipeline from the TUI: extract
/// candidates from a provided conversation (exercises the real Claude extractor when an Anthropic key
/// is configured), list pending candidates, and confirm/reject them.
/// </summary>
public static class SummarizationAdminEndpoints
{
    public record SmokeLine(string Author, string Text);
    public record SmokeRequest(string WorkItemKey, List<SmokeLine> Lines);
    public record CandidateDto(Guid Id, string WorkItemKey, string Kind, string Content, string? Evidence, double Confidence, string Status);

    public static void MapSummarizationAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/summarize");

        group.MapPost("/", async (SmokeRequest req, IConversationSummarizer svc, CancellationToken ct) =>
        {
            var lines = (req.Lines ?? new()).Select(l => new TranscriptLine(l.Author, l.Text, "")).ToList();
            var result = await svc.SmokeSummarizeAsync(req.WorkItemKey, lines, ct);
            return Results.Ok(new
            {
                outcome = result.Outcome,
                detail = result.Detail,
                candidates = result.Candidates.Select(ToDto),
            });
        });

        group.MapGet("/candidates/{key}", async (string key, JiraSyncDbContext db, CancellationToken ct) =>
        {
            var pending = await db.SummaryCandidates
                .Where(c => c.WorkItemKey == key && c.Status == CandidateStatus.Pending)
                .OrderByDescending(c => c.CreatedUtc).ToListAsync(ct);
            return Results.Ok(pending.Select(ToDto));
        });

        group.MapPost("/candidates/{id:guid}/confirm", async (Guid id, string? user, IConversationSummarizer svc, CancellationToken ct)
            => Results.Ok(new { outcome = await svc.ConfirmAsync(id, user ?? "tui", ct) }));

        group.MapPost("/candidates/{id:guid}/reject", async (Guid id, IConversationSummarizer svc, CancellationToken ct)
            => Results.Ok(new { outcome = await svc.RejectAsync(id, ct) }));
    }

    private static CandidateDto ToDto(SummaryCandidate c)
        => new(c.Id, c.WorkItemKey, c.Kind.ToString(), c.Content, c.Evidence, c.Confidence, c.Status.ToString());
}
