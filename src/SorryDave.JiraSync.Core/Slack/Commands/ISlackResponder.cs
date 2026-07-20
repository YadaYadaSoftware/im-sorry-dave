using System.Net.Http.Json;

namespace SorryDave.JiraSync.Core.Slack.Commands;

/// <summary>
/// Delivers a plugin's result back to Slack through a command's or interaction's <c>response_url</c>.
/// Abstracted so command dispatch — acknowledgement, background execution, failure reporting — is
/// testable without standing up HTTP.
///
/// Delivery is best-effort: a plugin's work has already happened by the time we report on it, so a
/// failed report must not surface as a handler failure.
/// </summary>
public interface ISlackResponder
{
    /// <summary>Post a follow-up ephemeral message to a response URL.</summary>
    Task RespondAsync(string responseUrl, string text, CancellationToken ct = default);

    /// <summary>Replace the original interactive message, which removes its buttons along with it.</summary>
    Task ReplaceOriginalAsync(string responseUrl, string text, object blocks, CancellationToken ct = default);
}

/// <inheritdoc cref="ISlackResponder"/>
public sealed class SlackResponder : ISlackResponder
{
    private readonly IHttpClientFactory _httpFactory;

    public SlackResponder(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public async Task RespondAsync(string responseUrl, string text, CancellationToken ct = default)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            await http.PostAsJsonAsync(responseUrl, new { response_type = "ephemeral", text }, ct);
        }
        catch { /* best-effort */ }
    }

    public async Task ReplaceOriginalAsync(string responseUrl, string text, object blocks, CancellationToken ct = default)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            await http.PostAsJsonAsync(responseUrl, new { replace_original = true, text, blocks }, ct);
        }
        catch { /* best-effort */ }
    }
}
