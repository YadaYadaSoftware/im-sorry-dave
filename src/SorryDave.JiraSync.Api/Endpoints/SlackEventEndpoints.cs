using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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

        // Slash command: /post — summarize since the last /post and post interactive candidate cards.
        // Extraction calls Claude (often > Slack's 3s ack limit), so we ACK immediately and do the
        // slow work in the background, delivering the result via the command's response_url.
        group.MapPost("/commands", async (HttpRequest req, IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpFactory, IOptions<SlackOptions> slack, TimeProvider clock) =>
        {
            var (ok, body) = await VerifyAsync(req, slack.Value.SigningSecret, clock);
            if (!ok) return Results.Unauthorized();

            var form = ParseForm(body);
            var channelId = form.GetValueOrDefault("channel_id") ?? "";
            var responseUrl = form.GetValueOrDefault("response_url");

            // Fire-and-forget with its own DI scope (the request scope disposes after we ACK). Do NOT
            // use the request CancellationToken here — it's cancelled once the response completes.
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var summarizer = scope.ServiceProvider.GetRequiredService<IConversationSummarizer>();
                var slackClient = scope.ServiceProvider.GetRequiredService<ISlackClient>();
                var opts = scope.ServiceProvider.GetRequiredService<IOptions<SlackOptions>>().Value;
                try
                {
                    var result = await summarizer.PostAsync(channelId, CancellationToken.None);
                    if (result.Outcome == "Extracted" && opts.IsConfigured)
                        foreach (var c in result.Candidates)
                            try { await slackClient.PostBlocksAsync(channelId, CandidateBlocks.Fallback(c), CandidateBlocks.Card(c), CancellationToken.None); }
                            catch (SlackApiException) { /* best-effort card posting */ }

                    var text = result.Outcome switch
                    {
                        "NotLinked" => ":warning: This channel isn't linked to a work item.",
                        "Empty" => "Nothing new to post since the last `/post`.",
                        "Extracted" => $":memo: Posted {result.Candidates.Count} candidate(s) — review and confirm below.",
                        _ => result.Detail ?? result.Outcome,
                    };
                    if (responseUrl is not null) await RespondAsync(httpFactory, responseUrl, text);
                }
                catch (Exception)
                {
                    if (responseUrl is not null) await RespondAsync(httpFactory, responseUrl, ":x: `/post` failed — see server logs.");
                }
            });

            // Immediate ACK (well within Slack's 3s window).
            return Results.Ok(new { response_type = "ephemeral", text = ":hourglass_flowing_sand: Summarizing this channel…" });
        });

        // Interactivity: confirm/reject a candidate. Replaces the card in place with the result.
        group.MapPost("/interactivity", async (HttpRequest req, IConversationSummarizer summarizer,
            IHttpClientFactory httpFactory, IOptions<SlackOptions> slack, TimeProvider clock, CancellationToken ct) =>
        {
            var (ok, body) = await VerifyAsync(req, slack.Value.SigningSecret, clock);
            if (!ok) return Results.Unauthorized();

            var form = ParseForm(body);
            if (!form.TryGetValue("payload", out var payloadJson)) return Results.BadRequest();
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var action = root.GetProperty("actions")[0];
            var actionId = action.GetProperty("action_id").GetString();        // "confirm" | "reject"
            var candidateId = Guid.Parse(action.GetProperty("value").GetString()!);
            var user = root.TryGetProperty("user", out var u) ? Str(u, "id") : null;
            var responseUrl = Str(root, "response_url");

            var outcome = actionId == "confirm"
                ? await summarizer.ConfirmAsync(candidateId, user, ct)
                : await summarizer.RejectAsync(candidateId, ct);

            // Report the result by replacing the original card (removes the buttons).
            var headline = outcome switch
            {
                "Confirmed" => $":white_check_mark: Confirmed and written back to Jira{(user is null ? "" : $" by <@{user}>")}.",
                "Rejected" => ":x: Rejected — not written back.",
                _ => $"Candidate {outcome}.",
            };
            if (responseUrl is not null)
                await ReplaceOriginalAsync(httpFactory, responseUrl, headline, CandidateBlocks.Result(headline), ct);

            return Results.Ok();
        });
    }

    /// <summary>Post a follow-up ephemeral message to a slash command's response_url.</summary>
    private static async Task RespondAsync(IHttpClientFactory httpFactory, string responseUrl, string text)
    {
        try
        {
            using var http = httpFactory.CreateClient();
            await http.PostAsJsonAsync(responseUrl, new { response_type = "ephemeral", text });
        }
        catch { /* best-effort */ }
    }

    /// <summary>Replace the original interactive message via Slack's response_url (no extra scope needed).</summary>
    private static async Task ReplaceOriginalAsync(IHttpClientFactory httpFactory, string responseUrl, string text, object blocks, CancellationToken ct)
    {
        try
        {
            using var http = httpFactory.CreateClient();
            await http.PostAsJsonAsync(responseUrl, new { replace_original = true, text, blocks }, ct);
        }
        catch { /* best-effort */ }
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
