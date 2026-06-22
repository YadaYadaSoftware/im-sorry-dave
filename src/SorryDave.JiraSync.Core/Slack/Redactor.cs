using System.Text.RegularExpressions;

namespace SorryDave.JiraSync.Core.Slack;

/// <summary>
/// Baseline redaction of well-known secret/token patterns, applied to both the text sent to Claude
/// and the content written back to Jira. On by default; the pattern set is configurable. Human
/// confirmation remains the final backstop.
/// </summary>
public sealed class Redactor
{
    public const string Mask = "[redacted]";

    // Well-known high-signal patterns. Conservative — avoids over-masking ordinary prose.
    private static readonly Regex[] Builtin =
    {
        new(@"xox[baprs]-[A-Za-z0-9-]{10,}", RegexOptions.Compiled),          // Slack tokens
        new(@"sk-[A-Za-z0-9_-]{16,}", RegexOptions.Compiled),                 // OpenAI-style keys
        new(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled),                      // AWS access key id
        new(@"ghp_[A-Za-z0-9]{20,}", RegexOptions.Compiled),                  // GitHub PAT
        new(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled),    // PEM private keys
        new(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", RegexOptions.Compiled), // JWTs
    };

    private readonly Regex[] _patterns;

    public Redactor(IEnumerable<string>? extraPatterns = null)
    {
        var extra = (extraPatterns ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new Regex(p, RegexOptions.Compiled));
        _patterns = Builtin.Concat(extra).ToArray();
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        foreach (var pattern in _patterns)
            text = pattern.Replace(text, Mask);
        return text;
    }
}
