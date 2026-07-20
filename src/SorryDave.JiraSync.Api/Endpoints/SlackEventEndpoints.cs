using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Slack;
using SorryDave.JiraSync.Core.Slack.Commands;
using SorryDave.JiraSync.Core.Summarization;

namespace SorryDave.JiraSync.Api.Endpoints;

/// <summary>
/// Inbound Slack endpoints: the Events API (message capture + URL verification), slash commands, and
/// interactivity. All requests are signature-verified against the Slack signing secret.
///
/// Slash commands and their interactivity actions are served by plugins; this endpoint only verifies,
/// parses, and delegates to <see cref="SlackCommandDispatcher"/>. Which commands exist is decided by
/// the <c>Slack:EnabledCommands</c> allow-list, not by anything here.
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

        // Slash commands. Dispatch is owned by SlackCommandDispatcher in Core, which resolves the command
        // name against the registry and owns the ack-then-background dance (Slack's 3s limit, the DI
        // scope, and the cancellation token that must not be the request's). This endpoint is a thin
        // adapter over it: verify, parse, delegate.
        group.MapPost("/commands", async (HttpRequest req, SlackCommandDispatcher dispatcher,
            IOptions<SlackOptions> slack, TimeProvider clock) =>
        {
            var (ok, body) = await VerifyAsync(req, slack.Value.SigningSecret, clock);
            if (!ok) return Results.Unauthorized();

            // Disabled and unowned commands come back refused, with no handler started.
            var dispatch = dispatcher.DispatchCommand(ParseForm(body));

            // Immediate ACK (well within Slack's 3s window); the handler, if any, runs on.
            return Results.Ok(new { response_type = "ephemeral", text = dispatch.AckText });
        });

        // Interactivity: dispatch by namespaced action id to the plugin that owns it. Replaces the card
        // in place with the result. Actions whose command is no longer registered — a stale button on an
        // old card — are refused rather than handled.
        group.MapPost("/interactivity", async (HttpRequest req, SlackCommandDispatcher dispatcher,
            ISlackResponder responder, IOptions<SlackOptions> slack, TimeProvider clock, CancellationToken ct) =>
        {
            var (ok, body) = await VerifyAsync(req, slack.Value.SigningSecret, clock);
            if (!ok) return Results.Unauthorized();

            var form = ParseForm(body);
            if (!form.TryGetValue("payload", out var payloadJson)) return Results.BadRequest();
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var action = root.GetProperty("actions")[0];
            var actionId = Str(action, "action_id");                           // "post:confirm" | "post:reject"
            var responseUrl = Str(root, "response_url");

            var context = new SlackActionContext(
                ActionName: "",                                                // filled in by the dispatcher
                Value: Str(action, "value"),
                UserId: root.TryGetProperty("user", out var u) ? Str(u, "id") : null,
                ChannelId: root.TryGetProperty("channel", out var c) ? Str(c, "id") : null,
                ResponseUrl: responseUrl);

            var result = await dispatcher.DispatchActionAsync(actionId, context, ct);

            // Report the result by replacing the original card (removes the buttons).
            if (responseUrl is not null)
                await responder.ReplaceOriginalAsync(responseUrl, result.Headline, CandidateBlocks.Result(result.Headline), ct);

            return Results.Ok();
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
