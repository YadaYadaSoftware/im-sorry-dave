using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.Summarization;

/// <summary>
/// Deterministic extractor used in tests and when no Anthropic key is configured. It does no AI: it
/// surfaces a single Summary candidate from the window and turns lines that look like decisions
/// ("we decided", "let's", "agreed") or questions ("?") into Decision/Answer candidates, so the
/// end-to-end pipeline (extract → confirm → write-back) is exercisable without Claude.
/// </summary>
public sealed class FakeDecisionExtractor : IDecisionExtractor
{
    public Task<IReadOnlyList<ExtractedCandidate>> ExtractAsync(
        string workItemKey, IReadOnlyList<TranscriptLine> window, CancellationToken ct = default)
    {
        var candidates = new List<ExtractedCandidate>();
        if (window.Count == 0)
            return Task.FromResult<IReadOnlyList<ExtractedCandidate>>(candidates);

        foreach (var line in window)
        {
            var text = line.Text;
            if (text.Contains("decide", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("agreed", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("let's", StringComparison.OrdinalIgnoreCase))
                candidates.Add(new ExtractedCandidate(WriteBackKind.Decision, text, $"{line.Author}: {text}", 0.8));
            else if (text.TrimEnd().EndsWith('?'))
                candidates.Add(new ExtractedCandidate(WriteBackKind.Answer, text, $"{line.Author}: {text}", 0.6));
        }

        var summary = string.Join(" ", window.TakeLast(5).Select(l => l.Text));
        candidates.Add(new ExtractedCandidate(WriteBackKind.Summary, summary, $"{window.Count} message(s)", 0.7));

        return Task.FromResult<IReadOnlyList<ExtractedCandidate>>(candidates);
    }
}
