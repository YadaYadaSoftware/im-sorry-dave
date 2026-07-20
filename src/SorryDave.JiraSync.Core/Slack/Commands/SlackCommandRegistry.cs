using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;

namespace SorryDave.JiraSync.Core.Slack.Commands;

/// <summary>
/// The set of slash commands actually served, and the lookup from an inbound command name or namespaced
/// action id to the plugin that owns it.
///
/// Registration is filtered by the <c>Slack:EnabledCommands</c> allow-list. A plugin present in the
/// container but absent from the allow-list is <see cref="SkippedCommands">skipped</see>, not served —
/// this is how a command is turned off without its implementation being touched.
/// </summary>
public interface ISlackCommandRegistry
{
    /// <summary>Plugins whose commands are served, ordered by name. Drives manifest generation.</summary>
    IReadOnlyList<ISlackCommandPlugin> RegisteredCommands { get; }

    /// <summary>Names of plugins found but not served, because the allow-list omits them.</summary>
    IReadOnlyList<string> SkippedCommands { get; }

    /// <summary>Resolve a registered plugin by command name. Leading slashes are tolerated, so Slack's
    /// <c>/post</c> matches a plugin named <c>post</c>. Returns false when the command is disabled or
    /// unowned — the caller refuses rather than guessing.</summary>
    bool TryResolveCommand(string? commandName, out ISlackCommandPlugin plugin);

    /// <summary>Resolve the plugin owning a namespaced action id, yielding the unqualified action name.
    /// Returns false for an un-namespaced id, an unknown plugin, a plugin whose command is not
    /// registered, or an action that plugin does not own.</summary>
    bool TryResolveAction(string? actionId, out ISlackCommandPlugin plugin, out string actionName);
}

/// <inheritdoc cref="ISlackCommandRegistry"/>
public sealed class SlackCommandRegistry : ISlackCommandRegistry
{
    private readonly Dictionary<string, ISlackCommandPlugin> _byName;

    public SlackCommandRegistry(IEnumerable<ISlackCommandPlugin> plugins, IOptions<SlackOptions> options)
    {
        var slack = options.Value;
        var all = plugins.ToList();

        _byName = all
            .Where(p => slack.IsCommandEnabled(p.Name))
            .ToDictionary(p => Normalize(p.Name), StringComparer.OrdinalIgnoreCase);

        RegisteredCommands = _byName.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        SkippedCommands = all
            .Where(p => !slack.IsCommandEnabled(p.Name))
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ISlackCommandPlugin> RegisteredCommands { get; }

    public IReadOnlyList<string> SkippedCommands { get; }

    public bool TryResolveCommand(string? commandName, out ISlackCommandPlugin plugin)
    {
        plugin = null!;
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        return _byName.TryGetValue(Normalize(commandName), out plugin!);
    }

    public bool TryResolveAction(string? actionId, out ISlackCommandPlugin plugin, out string actionName)
    {
        plugin = null!;
        actionName = "";

        if (!SlackActionId.TryParse(actionId, out var pluginName, out var parsedAction)) return false;
        if (!_byName.TryGetValue(Normalize(pluginName), out var owner)) return false;
        if (!owner.ActionNames.Contains(parsedAction, StringComparer.OrdinalIgnoreCase)) return false;

        plugin = owner;
        actionName = parsedAction;
        return true;
    }

    private static string Normalize(string name) => name.TrimStart('/');
}
