using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SorryDave.JiraSync.Core.Jira;

/// <summary>
/// Jira Cloud REST v3 client. Auth is HTTP Basic (email + API token). Transient-failure
/// retries are handled by <see cref="JiraRateLimitHandler"/> on the underlying HttpClient.
/// </summary>
public class JiraRestClient : IJiraClient
{
    private readonly HttpClient _http;
    private readonly ILogger<JiraRestClient> _logger;

    private const string Fields = "summary,status,assignee,reporter,issuetype,project,labels,description,updated";

    public JiraRestClient(HttpClient http, ILogger<JiraRestClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<JiraIssueData?> GetIssueAsync(string key, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"rest/api/3/issue/{Uri.EscapeDataString(key)}?fields={Fields}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccess(response, $"get issue {key}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return JiraIssueParser.Parse(doc.RootElement);
    }

    public async Task<IReadOnlyList<JiraIssueData>> SearchAsync(string jql, CancellationToken ct = default)
    {
        var results = new List<JiraIssueData>();
        var startAt = 0;
        const int pageSize = 100;

        while (true)
        {
            var url = $"rest/api/3/search?jql={Uri.EscapeDataString(jql)}&fields={Fields}&startAt={startAt}&maxResults={pageSize}";
            using var response = await _http.GetAsync(url, ct);
            await EnsureSuccess(response, "search issues");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            if (root.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
                foreach (var issue in issues.EnumerateArray())
                    results.Add(JiraIssueParser.Parse(issue));

            var total = root.TryGetProperty("total", out var t) ? t.GetInt32() : results.Count;
            startAt += pageSize;
            if (startAt >= total || !issues.EnumerateArray().Any()) break;
        }

        return results;
    }

    public async Task<string> AddCommentAsync(string issueKey, string body, CancellationToken ct = default)
    {
        var payload = new { body = AdfText.BuildDocument(body) };
        using var response = await _http.PostAsJsonAsync(
            $"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/comment", payload, ct);
        await EnsureSuccess(response, $"add comment to {issueKey}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public async Task UpdateCommentAsync(string issueKey, string commentId, string body, CancellationToken ct = default)
    {
        var payload = new { body = AdfText.BuildDocument(body) };
        using var response = await _http.PutAsJsonAsync(
            $"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/comment/{Uri.EscapeDataString(commentId)}", payload, ct);
        await EnsureSuccess(response, $"update comment {commentId} on {issueKey}");
    }

    private async Task EnsureSuccess(HttpResponseMessage response, string action)
    {
        if (response.IsSuccessStatusCode) return;

        var content = await response.Content.ReadAsStringAsync();
        var transient = JiraApiException.IsTransientStatus(response.StatusCode);
        _logger.LogError("Jira call failed ({Action}): {Status} {Body}", action, (int)response.StatusCode, content);
        throw new JiraApiException(
            $"Jira request failed ({action}): {(int)response.StatusCode}.",
            response.StatusCode, transient);
    }
}
