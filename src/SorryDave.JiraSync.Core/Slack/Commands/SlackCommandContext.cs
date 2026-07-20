namespace SorryDave.JiraSync.Core.Slack.Commands;

/// <summary>
/// What a plugin is told about a slash command invocation. Built by the host from the verified inbound
/// form payload, so plugins never parse Slack's wire format.
/// </summary>
/// <param name="CommandName">Command name without the leading slash, as resolved by the registry.</param>
/// <param name="ChannelId">Channel the command was invoked in.</param>
/// <param name="UserId">Slack user who invoked it, when Slack supplied one.</param>
/// <param name="Text">Everything typed after the command name; empty when nothing followed it.</param>
/// <param name="ResponseUrl">Where the result is delivered once the handler completes. Null when Slack
/// did not supply one, in which case the host has no way to report back.</param>
/// <param name="Form">The full verified form payload, for the rare field not surfaced above.</param>
public sealed record SlackCommandContext(
    string CommandName,
    string ChannelId,
    string? UserId,
    string Text,
    string? ResponseUrl,
    IReadOnlyDictionary<string, string> Form);

/// <summary>
/// What a plugin is told about one of its interactivity actions being triggered.
/// </summary>
/// <param name="ActionName">Unqualified action name (e.g. <c>confirm</c>), with the plugin namespace
/// already stripped by the host.</param>
/// <param name="Value">The action's <c>value</c> — for candidate cards, the candidate id.</param>
/// <param name="UserId">Slack user who clicked, when Slack supplied one.</param>
/// <param name="ChannelId">Channel the message lives in, when Slack supplied one.</param>
/// <param name="ResponseUrl">Used to replace the original message in place.</param>
public sealed record SlackActionContext(
    string ActionName,
    string? Value,
    string? UserId,
    string? ChannelId,
    string? ResponseUrl);
