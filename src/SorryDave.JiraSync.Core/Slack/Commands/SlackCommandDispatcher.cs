using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SorryDave.JiraSync.Core.Slack.Commands;

/// <summary>
/// Routes verified inbound Slack payloads to the plugin that owns them, and owns everything about
/// <em>how</em> a command runs so no plugin has to.
///
/// Slack requires acknowledgement within about three seconds, but handlers routinely exceed it — the
/// summarizer calls Claude. So a command is acknowledged immediately with the plugin's declared text and
/// the handler runs afterwards, delivering its result through the command's response URL. Two subtleties
/// live here rather than in every plugin:
///
/// <list type="bullet">
/// <item>The handler runs in a service scope this dispatcher creates and owns, because the inbound
/// request's scope is disposed the moment we acknowledge.</item>
/// <item>The handler is never given the request's cancellation token, which is cancelled at that same
/// moment. Doing so would cancel the work we just promised to do.</item>
/// </list>
///
/// The API endpoint is deliberately a thin adapter over this: verify the signature, parse the form,
/// delegate. That keeps dispatch, acknowledgement, and failure reporting testable.
/// </summary>
public sealed class SlackCommandDispatcher
{
    /// <summary>Reply when a command is disabled or unowned. Deliberately does not distinguish the two —
    /// a user has no use for the difference, and it avoids advertising commands that exist but are off.</summary>
    public const string UnavailableCommandText = "That command isn't available.";

    /// <summary>Reply when a button belongs to a command that is no longer served — typically a stale
    /// card still sitting in channel history from before the command was disabled.</summary>
    public const string UnavailableActionText = "That action is no longer available.";

    private readonly ISlackCommandRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISlackResponder _responder;
    private readonly ILogger<SlackCommandDispatcher> _logger;

    public SlackCommandDispatcher(
        ISlackCommandRegistry registry,
        IServiceScopeFactory scopeFactory,
        ISlackResponder responder,
        ILogger<SlackCommandDispatcher> logger)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _responder = responder;
        _logger = logger;
    }

    /// <summary>
    /// Dispatch a slash command from its verified form payload. Returns immediately with the text to
    /// acknowledge with; any handler runs in the background.
    /// </summary>
    public SlackDispatchResult DispatchCommand(IReadOnlyDictionary<string, string> form)
    {
        var commandName = form.GetValueOrDefault("command") ?? "";

        if (!_registry.TryResolveCommand(commandName, out var plugin))
        {
            _logger.LogInformation("Slash command {Command} is not registered; refusing.", commandName);
            return SlackDispatchResult.Refused(UnavailableCommandText);
        }

        var context = new SlackCommandContext(
            CommandName: plugin.Name,
            ChannelId: form.GetValueOrDefault("channel_id") ?? "",
            UserId: form.GetValueOrDefault("user_id"),
            Text: form.GetValueOrDefault("text") ?? "",
            ResponseUrl: form.GetValueOrDefault("response_url"),
            Form: form);

        return new SlackDispatchResult(plugin.AckText, Accepted: true, Completion: RunCommandAsync(context));
    }

    /// <summary>
    /// Dispatch an interactivity action from its namespaced action id. Actions are fast and run inside
    /// the request, so no background handoff is needed here.
    /// </summary>
    public async Task<SlackActionResult> DispatchActionAsync(
        string? actionId, SlackActionContext context, CancellationToken ct = default)
    {
        if (!_registry.TryResolveAction(actionId, out var plugin, out var actionName))
        {
            _logger.LogInformation("Interactivity action {ActionId} has no registered owner; refusing.", actionId);
            return SlackActionResult.Message(UnavailableActionText);
        }

        try
        {
            return await plugin.HandleActionAsync(context with { ActionName = actionName }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action {ActionId} failed.", actionId);
            return SlackActionResult.Message($":x: `{actionId}` failed — see server logs.");
        }
    }

    /// <summary>
    /// Run a command handler outside the inbound request. Owns its own scope and uses
    /// <see cref="CancellationToken.None"/> deliberately — see the type remarks.
    /// </summary>
    private async Task RunCommandAsync(SlackCommandContext context)
    {
        await Task.Yield(); // return to the caller so it can acknowledge before the handler runs

        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ISlackCommandRegistry>();

        string text;
        try
        {
            if (!registry.TryResolveCommand(context.CommandName, out var plugin))
                return; // registration changed under us; nothing useful to say

            var result = await plugin.HandleCommandAsync(context, CancellationToken.None);
            text = result.Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Slash command /{Command} failed.", context.CommandName);
            text = $":x: `/{context.CommandName}` failed — see server logs.";
        }

        if (context.ResponseUrl is not null)
            await _responder.RespondAsync(context.ResponseUrl, text, CancellationToken.None);
    }
}
