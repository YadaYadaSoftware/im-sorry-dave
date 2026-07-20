namespace SorryDave.JiraSync.Core.Slack.Commands;

/// <summary>
/// What a plugin's command handler produces. The host delivers <see cref="Text"/> to the invoking user
/// through the command's response URL once the handler completes.
/// </summary>
/// <param name="Text">Message shown to the invoking user.</param>
public sealed record SlackCommandResult(string Text)
{
    public static SlackCommandResult Message(string text) => new(text);
}

/// <summary>
/// What a plugin's action handler produces. The host replaces the original message with
/// <see cref="Headline"/>, which removes the buttons along with it.
/// </summary>
/// <param name="Headline">Message that replaces the acted-on card.</param>
public sealed record SlackActionResult(string Headline)
{
    public static SlackActionResult Message(string headline) => new(headline);
}

/// <summary>
/// The host's response to an inbound slash command: what to acknowledge with, whether a handler was
/// actually started, and — for tests — the background work so completion can be awaited.
/// </summary>
/// <param name="AckText">Acknowledgement posted immediately, inside Slack's deadline.</param>
/// <param name="Accepted">False when no registered plugin owns the command, in which case
/// <see cref="AckText"/> is the "not available" reply and no handler ran.</param>
/// <param name="Completion">The background handler. Production callers ignore it; tests await it to
/// observe work that by design outlives the request.</param>
public sealed record SlackDispatchResult(string AckText, bool Accepted, Task Completion)
{
    public static SlackDispatchResult Refused(string ackText) => new(ackText, false, Task.CompletedTask);
}
