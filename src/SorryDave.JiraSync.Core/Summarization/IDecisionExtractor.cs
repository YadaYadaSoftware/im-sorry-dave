using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.Summarization;

/// <summary>One message in a conversation window handed to the extractor.</summary>
public sealed record TranscriptLine(string Author, string Text, string Ts);

/// <summary>A structured candidate returned by extraction, before persistence/confirmation.</summary>
public sealed record ExtractedCandidate(WriteBackKind Kind, string Content, string? Evidence, double Confidence);

/// <summary>
/// Extracts decision/answer/summary candidates from a conversation window. Implemented by the Claude
/// client (<c>AnthropicDecisionExtractor</c>); a deterministic fake is used in tests and when no
/// Anthropic key is configured.
/// </summary>
public interface IDecisionExtractor
{
    Task<IReadOnlyList<ExtractedCandidate>> ExtractAsync(
        string workItemKey, IReadOnlyList<TranscriptLine> window, CancellationToken ct = default);
}
