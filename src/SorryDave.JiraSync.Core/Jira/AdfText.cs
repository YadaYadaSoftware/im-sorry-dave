using System.Text;
using System.Text.Json;

namespace SorryDave.JiraSync.Core.Jira;

/// <summary>
/// Minimal helpers for Atlassian Document Format (ADF), used by Jira Cloud REST v3.
/// We only need to (a) flatten an ADF doc to readable text and (b) build a simple ADF doc
/// from plain text (paragraphs split on blank lines).
/// </summary>
public static class AdfText
{
    /// <summary>Recursively collect all <c>text</c> nodes into a single string.</summary>
    public static string Flatten(JsonElement node)
    {
        var sb = new StringBuilder();
        Walk(node, sb);
        return sb.ToString().Trim();
    }

    /// <summary>Collect distinct accountIds of <c>mention</c> nodes (<c>attrs.id</c>) anywhere in the doc.</summary>
    public static List<string> CollectMentionAccountIds(JsonElement node)
    {
        var ids = new List<string>();
        WalkMentions(node, ids);
        return ids;
    }

    private static void WalkMentions(JsonElement node, List<string> ids)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String &&
                t.GetString() == "mention" &&
                node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object &&
                attrs.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                var value = id.GetString();
                if (!string.IsNullOrWhiteSpace(value) && !ids.Contains(value)) ids.Add(value!);
            }

            if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var child in content.EnumerateArray())
                    WalkMentions(child, ids);
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
                WalkMentions(child, ids);
        }
    }

    private static void Walk(JsonElement node, StringBuilder sb)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                sb.Append(text.GetString());

            if (node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String &&
                (t.GetString() is "paragraph" or "heading"))
                sb.Append('\n');

            if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var child in content.EnumerateArray())
                    Walk(child, sb);
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
                Walk(child, sb);
        }
    }

    /// <summary>Build a minimal ADF document object from plain text.</summary>
    public static object BuildDocument(string text)
    {
        var paragraphs = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.None);
        var content = new List<object>();
        foreach (var para in paragraphs)
        {
            content.Add(new
            {
                type = "paragraph",
                content = new[]
                {
                    new { type = "text", text = string.IsNullOrEmpty(para) ? " " : para }
                }
            });
        }

        return new { type = "doc", version = 1, content };
    }
}
