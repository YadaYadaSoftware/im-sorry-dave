using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.Summarization;

/// <summary>
/// Extracts candidates by calling the Anthropic Messages API (Claude). The transcript is sent with a
/// system prompt that constrains output to a JSON array of {kind, content, evidence, confidence};
/// the response is parsed defensively (malformed output → no candidates, logged) so a bad model
/// response never breaks the pipeline.
/// </summary>
public sealed class AnthropicDecisionExtractor : IDecisionExtractor
{
    private const string System =
        "You extract decisions, answers, and summaries from a Slack conversation about a work item. " +
        "Return ONLY a JSON array (no prose). Each element: " +
        "{\"kind\":\"Decision\"|\"Answer\"|\"Summary\",\"content\":string,\"evidence\":string,\"confidence\":number 0..1}. " +
        "Ground every item in the transcript; if nothing substantive was decided or answered, return just one Summary element.";

    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicDecisionExtractor> _logger;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public AnthropicDecisionExtractor(HttpClient http, IOptions<AnthropicOptions> options, ILogger<AnthropicDecisionExtractor> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExtractedCandidate>> ExtractAsync(
        string workItemKey, IReadOnlyList<TranscriptLine> window, CancellationToken ct = default)
    {
        if (window.Count == 0) return Array.Empty<ExtractedCandidate>();

        var transcript = string.Join("\n", window.Select(l => $"{l.Author}: {l.Text}"));
        var payload = new
        {
            model = _options.Model,
            max_tokens = 1024,
            system = System,
            messages = new[] { new { role = "user", content = $"Work item {workItemKey}.\nTranscript:\n{transcript}" } },
        };

        try
        {
            using var response = await _http.PostAsJsonAsync("v1/messages", payload, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            // Concatenate text blocks from the response content.
            var text = string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
                .Where(b => b.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(b => b.GetProperty("text").GetString()));

            return ParseCandidates(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anthropic extraction failed for {Key}; returning no candidates.", workItemKey);
            return Array.Empty<ExtractedCandidate>();
        }
    }

    private IReadOnlyList<ExtractedCandidate> ParseCandidates(string text)
    {
        var json = ExtractJsonArray(text);
        if (json is null) { _logger.LogWarning("Claude output was not a JSON array; no candidates."); return Array.Empty<ExtractedCandidate>(); }

        try
        {
            var raw = JsonSerializer.Deserialize<List<RawCandidate>>(json, Json) ?? new();
            return raw
                .Where(r => !string.IsNullOrWhiteSpace(r.Content))
                .Select(r => new ExtractedCandidate(
                    Enum.TryParse<WriteBackKind>(r.Kind, ignoreCase: true, out var k) ? k : WriteBackKind.Summary,
                    r.Content!, r.Evidence, Math.Clamp(r.Confidence, 0, 1)))
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse Claude candidate JSON; no candidates.");
            return Array.Empty<ExtractedCandidate>();
        }
    }

    /// <summary>Pull the first top-level JSON array out of the model text (tolerates stray prose).</summary>
    private static string? ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private sealed class RawCandidate
    {
        public string? Kind { get; set; }
        public string? Content { get; set; }
        public string? Evidence { get; set; }
        public double Confidence { get; set; }
    }
}
