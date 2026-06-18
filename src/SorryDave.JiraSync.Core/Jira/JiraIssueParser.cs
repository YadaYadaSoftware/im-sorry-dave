using System.Globalization;
using System.Text.Json;

namespace SorryDave.JiraSync.Core.Jira;

/// <summary>
/// Parses a Jira <c>issue</c> JSON element (from REST or webhook) into <see cref="JiraIssueData"/>.
/// Defensive against missing/null fields so a partial payload never throws.
/// </summary>
public static class JiraIssueParser
{
    public static JiraIssueData Parse(JsonElement issue)
    {
        var key = GetString(issue, "key")
            ?? throw new FormatException("Jira issue payload is missing 'key'.");

        var fields = issue.TryGetProperty("fields", out var f) ? f : default;

        var projectKey = TryGetNested(fields, "project", "key") ?? DeriveProjectKey(key);
        var issueType = TryGetNested(fields, "issuetype", "name") ?? "Unknown";
        var status = TryGetNested(fields, "status", "name") ?? "Unknown";
        var summary = GetString(fields, "summary") ?? "(no summary)";
        var description = ExtractDescription(fields);

        string? assigneeAccountId = null, assigneeDisplay = null, reporterDisplay = null;
        if (fields.ValueKind == JsonValueKind.Object)
        {
            if (fields.TryGetProperty("assignee", out var a) && a.ValueKind == JsonValueKind.Object)
            {
                assigneeAccountId = GetString(a, "accountId");
                assigneeDisplay = GetString(a, "displayName");
            }
            if (fields.TryGetProperty("reporter", out var r) && r.ValueKind == JsonValueKind.Object)
                reporterDisplay = GetString(r, "displayName");
        }

        var labels = new List<string>();
        if (fields.ValueKind == JsonValueKind.Object &&
            fields.TryGetProperty("labels", out var l) && l.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in l.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) labels.Add(item.GetString()!);
        }

        var updated = ParseTimestamp(GetString(fields, "updated"));

        return new JiraIssueData
        {
            Key = key,
            ProjectKey = projectKey,
            IssueType = issueType,
            Status = status,
            AssigneeAccountId = assigneeAccountId,
            AssigneeDisplayName = assigneeDisplay,
            ReporterDisplayName = reporterDisplay,
            Summary = summary,
            Description = description,
            Labels = labels,
            Updated = updated
        };
    }

    private static string DeriveProjectKey(string issueKey)
    {
        var dash = issueKey.LastIndexOf('-');
        return dash > 0 ? issueKey[..dash] : issueKey;
    }

    /// <summary>
    /// Description may be a plain string (REST v2) or an ADF document (REST v3). Extract a
    /// readable text approximation in both cases.
    /// </summary>
    private static string? ExtractDescription(JsonElement fields)
    {
        if (fields.ValueKind != JsonValueKind.Object ||
            !fields.TryGetProperty("description", out var d))
            return null;

        return d.ValueKind switch
        {
            JsonValueKind.String => d.GetString(),
            JsonValueKind.Object => AdfText.Flatten(d),
            _ => null
        };
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.ToUniversalTime();
        return DateTimeOffset.MinValue;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static string? TryGetNested(JsonElement element, string parent, string child)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(parent, out var p) &&
            p.ValueKind == JsonValueKind.Object)
            return GetString(p, child);
        return null;
    }
}
