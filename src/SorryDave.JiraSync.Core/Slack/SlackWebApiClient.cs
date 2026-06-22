using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SorryDave.JiraSync.Core.Slack;

/// <summary>A non-ok Slack Web API response (the <c>error</c> field), e.g. <c>name_taken</c>.</summary>
public sealed class SlackApiException : Exception
{
    public string Error { get; }
    public SlackApiException(string error) : base($"Slack API error: {error}") => Error = error;
}

/// <summary>
/// <see cref="ISlackClient"/> over the Slack Web API. The bot token is applied as a default
/// Authorization header by DI. Honors <c>429</c> <c>Retry-After</c> (and surfaces <c>rate_limited</c>),
/// and raises <see cref="SlackApiException"/> for any non-ok response so callers can branch on the code.
/// </summary>
public sealed class SlackWebApiClient : ISlackClient
{
    private const int MaxRetries = 4;
    private readonly HttpClient _http;

    public SlackWebApiClient(HttpClient http) => _http = http;

    public async Task<SlackChannel> CreateChannelAsync(string name, CancellationToken ct = default)
    {
        var json = await PostAsync("conversations.create", new { name }, ct);
        return ReadChannel(json.GetProperty("channel"));
    }

    public async Task<SlackChannel?> FindChannelByNameAsync(string name, CancellationToken ct = default)
    {
        string? cursor = null;
        do
        {
            var json = await PostAsync("conversations.list", new
            {
                types = "public_channel",
                exclude_archived = false,
                limit = 200,
                cursor,
            }, ct);

            foreach (var ch in json.GetProperty("channels").EnumerateArray())
                if (string.Equals(ch.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                    return ReadChannel(ch);

            cursor = json.TryGetProperty("response_metadata", out var meta)
                     && meta.TryGetProperty("next_cursor", out var nc) ? nc.GetString() : null;
        }
        while (!string.IsNullOrEmpty(cursor));

        return null;
    }

    public async Task<SlackChannel?> GetChannelInfoAsync(string channelId, CancellationToken ct = default)
    {
        try
        {
            var json = await PostAsync("conversations.info", new { channel = channelId }, ct);
            return ReadChannel(json.GetProperty("channel"));
        }
        catch (SlackApiException ex) when (ex.Error is "channel_not_found")
        {
            return null;
        }
    }

    public async Task<string> PostMessageAsync(string channelId, string text, CancellationToken ct = default)
    {
        var json = await PostAsync("chat.postMessage", new { channel = channelId, text }, ct);
        return json.GetProperty("ts").GetString()!;
    }

    public Task SetTopicAsync(string channelId, string topic, CancellationToken ct = default)
        => PostAsync("conversations.setTopic", new { channel = channelId, topic }, ct);

    public Task SetPurposeAsync(string channelId, string purpose, CancellationToken ct = default)
        => PostAsync("conversations.setPurpose", new { channel = channelId, purpose }, ct);

    public Task ArchiveAsync(string channelId, CancellationToken ct = default)
        => PostAsync("conversations.archive", new { channel = channelId }, ct);

    public Task UnarchiveAsync(string channelId, CancellationToken ct = default)
        => PostAsync("conversations.unarchive", new { channel = channelId }, ct);

    public Task PinMessageAsync(string channelId, string messageTs, CancellationToken ct = default)
        => PostAsync("pins.add", new { channel = channelId, timestamp = messageTs }, ct);

    public async Task<string?> LookupUserIdByEmailAsync(string email, CancellationToken ct = default)
    {
        try
        {
            var json = await PostAsync("users.lookupByEmail", new { email }, ct);
            return json.GetProperty("user").GetProperty("id").GetString();
        }
        catch (SlackApiException ex) when (ex.Error == "users_not_found")
        {
            return null;
        }
    }

    public async Task InviteAsync(string channelId, string userId, CancellationToken ct = default)
    {
        try
        {
            await PostAsync("conversations.invite", new { channel = channelId, users = userId }, ct);
        }
        catch (SlackApiException ex) when (ex.Error is "already_in_channel" or "cant_invite_self")
        {
            // benign — treat as success
        }
    }

    private static SlackChannel ReadChannel(JsonElement ch) => new(
        ch.GetProperty("id").GetString()!,
        ch.GetProperty("name").GetString()!,
        ch.TryGetProperty("is_archived", out var a) && a.GetBoolean());

    private async Task<JsonElement> PostAsync(string method, object payload, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var content = JsonContent.Create(payload);
            using var response = await _http.PostAsync(method, content, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                await Task.Delay(wait, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            if (!json.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var error = json.TryGetProperty("error", out var e) ? e.GetString() ?? "unknown_error" : "unknown_error";
                // Slack may also return rate_limited in the body with a Retry-After header.
                if (error == "rate_limited" && attempt < MaxRetries)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    await Task.Delay(wait, ct);
                    continue;
                }
                throw new SlackApiException(error);
            }

            return json;
        }
    }
}
