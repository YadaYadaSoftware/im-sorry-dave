using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Slack;
using SorryDave.JiraSync.Core.Summarization;

namespace SorryDave.JiraSync.Api.Endpoints;

/// <summary>
/// Inbound Slack endpoints: the Events API (message capture + URL verification), the <c>/post</c>
/// slash command, and interactivity (candidate confirm/reject). All requests are signature-verified
/// against the Slack signing secret.
/// </summary>
public static class SlackEventEndpoints
{
    public static void MapSlackEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/slack");

        // Events API: URL verification challenge + message events.
        group.MapPost("/events", async (HttpRequest req, IConversationSummarizer summarizer,
            IOptions<SlackOptions> slack, TimeProvider clock, CancellationToken ct) =>
        {
            var (ok, body) = await VerifyAsync(req, slack.Value.SigningSecret, clock);
            if (!ok) return Results.Unauthorized();

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == "url_verification")
                return Results.Ok(new { challenge = root.GetProperty("challenge").GetString() });

            if (type == "event_callback" && root.TryGetProperty("event", out var ev) &&
                ev.TryGetProperty("type", out var et) && et.GetString() == "message")
            {
                // Skip bot messages and edits-of-our-own; capture human messages.
                if (!ev.TryGetProperty("bot_id", out _))
                {
                    var msg = new IncomingMessage(
                        ChannelId: Str(ev, "channel") ?? "",
                        Ts: Str(ev, "ts") ?? "",
                        ThreadTs: Str(ev, "thread_ts"),
                        AuthorId: Str(ev, "user"),
                        Text: Str(ev, "text") ?? "",
                        Deleted: Str(ev, "subtype") == "message_deleted");
                    if (msg.ChannelId.Length > 0 && msg.Ts.Length > 0)
                        await summarizer.CaptureAsync(msg, ct);
                }
            }

            return Results.Ok(); // ack within 3s; Slack retries otherwise
        });

        // Slash command: /post — summarize the conversation since the last successful /post.
        group.MapPost("/commands", async (HttpRequest req, IConversationSummarizer summarizer,
            IOptions<SlackOptions> slack, TimeProvider clock, CancellationToken ct) =>
        {
            var (ok, body) = await VerifyAsync(req, slack.Value.SigningSecret, clock);
            if (!ok) return Results.Unauthorized();

            var form = ParseForm(body);
            var channelId = form.GetValueOrDefault("channel_id") ?? "";
            var result = await summarizer.PostAsync(channelId, ct);

            var text = result.Outcome switch
            {
                "NotLinked" => ":warning: This channel isn't linked to a work item.",
                "Empty" => "Nothing new to post since the last `/post`.",
                "Extracted" => $":memo: {result.Candidates.Count} candidate(s) from the conversation — review and confirm.",
                _ => result.Detail ?? result.Outcome,
            };
            // Ephemeral response to the invoking user.
            return Results.Ok(new { response_type = "ephemeral", text });
        });

        // Interactivity: confirm/reject a candidate (payload=<url-encoded JSON>).
        group.MapPost("/interactivity", async (HttpRequest req, IConversationSummarizer summarizer,
            IOptions<SlackOptions> slack, TimeProvider clock, CancellationToken ct) =>
        {
            var (ok, body) = await VerifyAsync(req, slack.Value.SigningSecret, clock);
            if (!ok) return Results.Unauthorized();

            var form = ParseForm(body);
            if (!form.TryGetValue("payload", out var payloadJson)) return Results.BadRequest();
            using var doc = JsonDocument.Parse(payloadJson);
            var action = doc.RootElement.GetProperty("actions")[0];
            var actionId = action.GetProperty("action_id").GetString();        // "confirm" | "reject"
            var candidateId = Guid.Parse(action.GetProperty("value").GetString()!);
            var user = doc.RootElement.TryGetProperty("user", out var u) ? Str(u, "id") : null;

            var outcome = actionId == "confirm"
                ? await summarizer.ConfirmAsync(candidateId, user, ct)
                : await summarizer.RejectAsync(candidateId, ct);

            return Results.Ok(new { text = $"Candidate {outcome}." });
        });
    }

    private static async Task<(bool Ok, string Body)> VerifyAsync(HttpRequest req, string? signingSecret, TimeProvider clock)
    {
        req.EnableBuffering();
        using var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        req.Body.Position = 0;

        var sig = req.Headers["X-Slack-Signature"].ToString();
        var ts = req.Headers["X-Slack-Request-Timestamp"].ToString();
        var ok = SlackSignatureVerifier.IsValid(signingSecret, sig, ts, body, clock.GetUtcNow());
        return (ok, body);
    }

    private static Dictionary<string, string> ParseForm(string body)
        => body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "");

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
