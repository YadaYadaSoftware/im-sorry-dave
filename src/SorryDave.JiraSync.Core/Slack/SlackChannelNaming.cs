using System.Text;

namespace SorryDave.JiraSync.Core.Slack;

/// <summary>
/// Derives a Slack channel name from a work item: <c>&lt;key&gt;-&lt;summary-slug&gt;</c>, normalized to
/// Slack's rules — lowercase, alphanumeric runs separated by single hyphens, ≤ 80 chars. The work-item
/// key is always preserved (for discoverability/dedup); the summary slug is truncated to fit, and a
/// deterministic suffix is appended to disambiguate collisions.
/// </summary>
public static class SlackChannelNaming
{
    public const int MaxLength = 80;

    /// <param name="suffix">Collision disambiguator (e.g. "2"); appended as <c>-2</c>.</param>
    public static string Derive(string key, string? summary, string? suffix = null)
    {
        var keySlug = Slugify(key);
        var suff = string.IsNullOrWhiteSpace(suffix) ? "" : "-" + Slugify(suffix!);

        // Key (+ suffix) alone over the limit is pathological — keep the key, truncate hard.
        if (keySlug.Length + suff.Length >= MaxLength)
        {
            var head = (keySlug + suff);
            return head.Length <= MaxLength ? head : head[..MaxLength].Trim('-');
        }

        var summarySlug = Slugify(summary ?? "");
        var budget = MaxLength - keySlug.Length - suff.Length - 1; // -1 for the key/summary hyphen
        if (summarySlug.Length > budget)
            summarySlug = summarySlug[..budget].Trim('-');

        return summarySlug.Length > 0
            ? $"{keySlug}-{summarySlug}{suff}"
            : keySlug + suff;
    }

    private static string Slugify(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastHyphen = false;
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); lastHyphen = false; }
            else if (sb.Length > 0 && !lastHyphen) { sb.Append('-'); lastHyphen = true; }
        }
        return sb.ToString().Trim('-');
    }
}
