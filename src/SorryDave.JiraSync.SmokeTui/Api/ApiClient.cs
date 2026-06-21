using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SorryDave.JiraSync.SmokeTui.Api;

/// <summary>HTTP client for the jira-sync-core API.</summary>
public class ApiClient : IApiClient
{
    private readonly HttpClient _http;
    private readonly string? _webhookSecret;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ApiClient(HttpClient http, string? webhookSecret = null)
    {
        _http = http;
        _webhookSecret = webhookSecret;
    }

    public string BaseAddress => _http.BaseAddress?.ToString() ?? "(unset)";

    public async Task<IReadOnlyList<WorkItemDto>> GetWorkItemsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<WorkItemDto>>("workitems", Json, ct) ?? new();

    public Task<int> BackfillAsync(CancellationToken ct = default)
        => PostForIntAsync("admin/backfill", "mirrored", ct);

    public Task<int> ReconcileAsync(CancellationToken ct = default)
        => PostForIntAsync("admin/reconcile", "refreshed", ct);

    public Task<int> DrainWriteBackAsync(CancellationToken ct = default)
        => PostForIntAsync("admin/drain-writeback", "sent", ct);

    public async Task<WriteBackDto> SubmitWriteBackAsync(string workItemKey, WriteBackRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(
            $"workitems/{Uri.EscapeDataString(workItemKey)}/writeback", request, Json, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WriteBackDto>(Json, ct))!;
    }

    public async Task<IReadOnlyList<WriteBackDto>> GetWriteBacksAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<WriteBackDto>>("writeback", Json, ct) ?? new();

    public async Task SimulateWebhookAsync(WorkItemDto item, CancellationToken ct = default)
    {
        var envelope = new
        {
            webhookEvent = "jira:issue_updated",
            issue = new
            {
                key = item.Key,
                fields = new
                {
                    project = new { key = item.ProjectKey ?? DeriveProject(item.Key) },
                    issuetype = new { name = item.IssueType ?? "Task" },
                    status = new { name = "In Review" },
                    summary = item.Summary ?? item.Key,
                    updated = DateTimeOffset.UtcNow.ToString("o")
                }
            }
        };

        // Secured backends (e.g. the deployed AWS API) require the shared secret as ?secret=;
        // unsecured local backends accept the request without it.
        var path = string.IsNullOrEmpty(_webhookSecret)
            ? "webhooks/jira"
            : "webhooks/jira?secret=" + Uri.EscapeDataString(_webhookSecret);

        using var response = await _http.PostAsJsonAsync(path, envelope, Json, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<CommentDto>?> GetFakeCommentsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("debug/jira-comments", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // real Jira backend
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CommentDto>>(Json, ct) ?? new();
    }

    private async Task<int> PostForIntAsync(string path, string property, CancellationToken ct)
    {
        using var response = await _http.PostAsync(path, content: null, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty(property, out var v) && v.TryGetInt32(out var n) ? n : 0;
    }

    private static string DeriveProject(string key)
    {
        var dash = key.LastIndexOf('-');
        return dash > 0 ? key[..dash] : key;
    }
}
