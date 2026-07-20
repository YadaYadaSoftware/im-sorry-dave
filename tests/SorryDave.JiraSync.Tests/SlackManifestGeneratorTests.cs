using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Slack.Commands;

namespace SorryDave.JiraSync.Tests;

public class SlackManifestGeneratorTests
{
    private const string Url = "https://jsg.appcloud.systems/slack/commands";

    [Fact]
    public void Renders_exactly_the_registered_commands()
    {
        var registry = new SlackCommandRegistry(
            new[] { new FakeCommandPlugin("post"), new FakeCommandPlugin("catchup") },
            Options.Create(SlackCommandHarness.Enabling("post", "catchup")));

        var yaml = SlackManifestGenerator.RenderSlashCommands(registry.RegisteredCommands, Url);

        Assert.Contains("- command: /post", yaml);
        Assert.Contains("- command: /catchup", yaml);
        Assert.Contains("description: post description", yaml);
        Assert.Contains($"url: {Url}", yaml);
    }

    [Fact]
    public void Omits_a_disabled_command()
    {
        var registry = new SlackCommandRegistry(
            new[] { new FakeCommandPlugin("post"), new FakeCommandPlugin("catchup") },
            Options.Create(SlackCommandHarness.Enabling("catchup")));

        var yaml = SlackManifestGenerator.RenderSlashCommands(registry.RegisteredCommands, Url);

        Assert.DoesNotContain("/post", yaml);
        Assert.Contains("- command: /catchup", yaml);
    }

    [Fact]
    public void Emits_no_slash_commands_block_when_nothing_is_registered()
    {
        var registry = new SlackCommandRegistry(
            new[] { new FakeCommandPlugin("post") }, Options.Create(new SlackOptions()));

        var yaml = SlackManifestGenerator.RenderSlashCommands(registry.RegisteredCommands, Url);

        Assert.DoesNotContain("slash_commands:", yaml);
        Assert.Contains(SlackManifestGenerator.BeginMarker, yaml);
        Assert.Contains(SlackManifestGenerator.EndMarker, yaml);
    }

    [Fact]
    public void Commands_are_rendered_in_a_stable_order()
    {
        var plugins = new[] { new FakeCommandPlugin("post"), new FakeCommandPlugin("catchup") };
        var options = Options.Create(SlackCommandHarness.Enabling("post", "catchup"));

        var first = SlackManifestGenerator.RenderSlashCommands(
            new SlackCommandRegistry(plugins, options).RegisteredCommands, Url);
        var second = SlackManifestGenerator.RenderSlashCommands(
            new SlackCommandRegistry(plugins.Reverse().ToArray(), options).RegisteredCommands, Url);

        Assert.Equal(first, second);
        Assert.True(first.IndexOf("/catchup", StringComparison.Ordinal)
                    < first.IndexOf("/post", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyTo_replaces_only_the_generated_region()
    {
        var manifest = $"header: keep\n{SlackManifestGenerator.BeginMarker}\n  stale: true\n{SlackManifestGenerator.EndMarker}\nfooter: keep\n";

        var updated = SlackManifestGenerator.ApplyTo(
            manifest, new[] { new FakeCommandPlugin("post") }, Url);

        Assert.Contains("header: keep", updated);
        Assert.Contains("footer: keep", updated);
        Assert.DoesNotContain("stale: true", updated);
        Assert.Contains("- command: /post", updated);
    }

    [Fact]
    public void ApplyTo_fails_when_the_markers_are_missing()
        => Assert.Throws<InvalidOperationException>(
            () => SlackManifestGenerator.ApplyTo("no markers here", Array.Empty<ISlackCommandPlugin>(), Url));

    // --- Drift: the committed manifest must match what the registry would generate ---

    [Fact]
    public void Committed_manifest_matches_the_generated_output()
    {
        var manifestPath = Path.Combine(RepoRoot(), "docs", "slack-app-manifest.yaml");
        var committed = File.ReadAllText(manifestPath);

        // The shipped configuration enables no commands, so the generated region must be empty.
        var registry = new SlackCommandRegistry(AllPlugins(), Options.Create(ShippedOptions()));
        var expected = SlackManifestGenerator.ApplyTo(committed, registry.RegisteredCommands, Url);

        Assert.Equal(Normalize(expected), Normalize(committed));
    }

    [Fact]
    public void Committed_manifest_retains_the_commands_scope_while_no_command_is_enabled()
    {
        // Retaining the scope is what makes re-enabling a manifest re-upload rather than a reinstall.
        var committed = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "slack-app-manifest.yaml"));

        Assert.Contains("- commands", committed);
        Assert.DoesNotContain("- command: /", committed);
    }

    /// <summary>Every plugin the app registers, so the drift check reflects the real command surface.</summary>
    private static ISlackCommandPlugin[] AllPlugins() => new ISlackCommandPlugin[]
    {
        new FakeCommandPlugin(Core.Slack.Commands.Plugins.PostCommandPlugin.CommandName),
    };

    /// <summary>The allow-list as shipped in appsettings.json.</summary>
    private static SlackOptions ShippedOptions()
    {
        var appsettings = Path.Combine(RepoRoot(), "src", "SorryDave.JiraSync.Api", "appsettings.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appsettings));

        var enabled = doc.RootElement
            .GetProperty("Slack")
            .GetProperty("EnabledCommands")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        return new SlackOptions { EnabledCommands = enabled };
    }

    private static string Normalize(string yaml) => yaml.Replace("\r\n", "\n").TrimEnd();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "im-sorry-dave.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
