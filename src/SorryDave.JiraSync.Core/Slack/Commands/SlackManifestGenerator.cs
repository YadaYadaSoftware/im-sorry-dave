using System.Text;

namespace SorryDave.JiraSync.Core.Slack.Commands;

/// <summary>
/// Renders the Slack app manifest's <c>slash_commands</c> block from the registered command set.
///
/// The relationship is one-directional: configuration decides the registry, the registry generates the
/// manifest, and the app never reads the manifest back to decide what to serve. The two layers do
/// different jobs — configuration makes a command <em>fail</em> if typed, the manifest makes it
/// <em>disappear</em> from Slack's autocomplete — and both are needed for a command to be truly gone.
///
/// Command list changes never alter OAuth scopes (the <c>commands</c> scope covers every slash command),
/// so regenerating this block means a manifest re-upload, not an app reinstall.
/// </summary>
public static class SlackManifestGenerator
{
    /// <summary>Marker delimiting the generated region inside the committed manifest, so the file's
    /// hand-written header, scopes, and settings survive regeneration.</summary>
    public const string BeginMarker = "  # BEGIN generated slash_commands — regenerate; do not hand-edit";

    /// <inheritdoc cref="BeginMarker"/>
    public const string EndMarker = "  # END generated slash_commands";

    /// <summary>
    /// Render the <c>slash_commands</c> block, including the surrounding markers. Emits nothing between
    /// the markers when no commands are registered, since Slack accepts a manifest with no commands.
    /// </summary>
    public static string RenderSlashCommands(IEnumerable<ISlackCommandPlugin> commands, string requestUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BeginMarker);

        var ordered = commands.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (ordered.Count > 0)
        {
            sb.AppendLine("  slash_commands:");
            foreach (var command in ordered)
            {
                sb.AppendLine($"    - command: /{command.Name}");
                sb.AppendLine($"      url: {requestUrl}");
                sb.AppendLine($"      description: {command.Description}");
                sb.AppendLine("      should_escape: false");
            }
        }

        sb.Append(EndMarker);
        return sb.ToString();
    }

    /// <summary>
    /// Replace the generated region of an existing manifest with a freshly rendered one, leaving every
    /// hand-written part of the file untouched.
    /// </summary>
    public static string ApplyTo(string manifestYaml, IEnumerable<ISlackCommandPlugin> commands, string requestUrl)
    {
        var begin = manifestYaml.IndexOf(BeginMarker, StringComparison.Ordinal);
        var end = manifestYaml.IndexOf(EndMarker, StringComparison.Ordinal);
        if (begin < 0 || end < 0 || end < begin)
            throw new InvalidOperationException(
                $"Manifest is missing the generated-region markers ('{BeginMarker}' / '{EndMarker}').");

        var rendered = RenderSlashCommands(commands, requestUrl);
        return manifestYaml[..begin] + rendered + manifestYaml[(end + EndMarker.Length)..];
    }
}
