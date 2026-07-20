namespace SorryDave.JiraSync.Core.Slack.Commands;

/// <summary>
/// A Slack slash command and the interactivity actions it produces. Pluggable: every slash command the
/// system serves is implemented behind this contract, and none is wired directly into the HTTP endpoint.
///
/// The unit is a <em>feature</em>, not a bare command — a plugin owns both its command and the buttons
/// that command posts, so the two are enabled and disabled together. Disabling a command therefore also
/// disables the actions it produced, including buttons on messages already sitting in channel history.
///
/// A plugin is registered only when its <see cref="Name"/> appears in <c>Slack:EnabledCommands</c>.
/// Absence means disabled: existing in the container is never sufficient to be served.
/// </summary>
public interface ISlackCommandPlugin
{
    /// <summary>Command name without the leading slash (e.g. <c>post</c>). Matched case-insensitively.</summary>
    string Name { get; }

    /// <summary>Human-readable description; becomes the command's description in the generated manifest.</summary>
    string Description { get; }

    /// <summary>Text acknowledging the invocation. The host posts this inside Slack's acknowledgement
    /// deadline, before <see cref="HandleCommandAsync"/> runs.</summary>
    string AckText { get; }

    /// <summary>Unqualified names of the interactivity actions this plugin owns (e.g. <c>confirm</c>).
    /// The host qualifies them with the plugin name — see <see cref="SlackActionId"/>. Empty for
    /// commands that post no buttons.</summary>
    IReadOnlyCollection<string> ActionNames { get; }

    /// <summary>Run the command. Called by the host outside the inbound request's lifetime, in its own
    /// service scope, with a cancellation token that is not tied to the request.</summary>
    Task<SlackCommandResult> HandleCommandAsync(SlackCommandContext context, CancellationToken ct = default);

    /// <summary>Handle one of this plugin's owned actions. <paramref name="context"/> carries the
    /// unqualified action name.</summary>
    Task<SlackActionResult> HandleActionAsync(SlackActionContext context, CancellationToken ct = default);
}

/// <summary>
/// Interactivity action ids are namespaced <c>plugin:action</c> so two plugins cannot claim the same
/// identifier and the owning plugin is resolvable from the id alone, with no central action table.
///
/// Un-namespaced ids (from cards posted before commands were pluggable) parse as unowned and are
/// refused rather than dispatched — the desired outcome for a stale button on an old message.
/// </summary>
public static class SlackActionId
{
    private const char Separator = ':';

    /// <summary>Build the namespaced id for a plugin's action, e.g. <c>("post", "confirm")</c> → <c>post:confirm</c>.</summary>
    public static string Qualify(string pluginName, string actionName) => $"{pluginName}{Separator}{actionName}";

    /// <summary>Split a namespaced id. Returns false for null, empty, or un-namespaced ids.</summary>
    public static bool TryParse(string? actionId, out string pluginName, out string actionName)
    {
        pluginName = "";
        actionName = "";
        if (string.IsNullOrWhiteSpace(actionId)) return false;

        var separator = actionId.IndexOf(Separator);
        if (separator <= 0 || separator == actionId.Length - 1) return false;

        pluginName = actionId[..separator];
        actionName = actionId[(separator + 1)..];
        return true;
    }
}
