using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Slack.Commands;

namespace SorryDave.JiraSync.Tests;

public class SlackCommandRegistryTests
{
    private static SlackCommandRegistry Registry(SlackOptions options, params ISlackCommandPlugin[] plugins)
        => new(plugins, Options.Create(options));

    // --- Registration is decided by the allow-list, not by the container ---

    [Fact]
    public void Registers_a_command_named_in_the_allow_list()
    {
        var registry = Registry(SlackCommandHarness.Enabling("post"), new FakeCommandPlugin("post"));

        Assert.Equal(new[] { "post" }, registry.RegisteredCommands.Select(p => p.Name));
        Assert.Empty(registry.SkippedCommands);
    }

    [Fact]
    public void Does_not_register_a_plugin_absent_from_the_allow_list()
    {
        var registry = Registry(SlackCommandHarness.Enabling("catchup"), new FakeCommandPlugin("post"));

        Assert.Empty(registry.RegisteredCommands);
        Assert.Equal(new[] { "post" }, registry.SkippedCommands);
        Assert.False(registry.TryResolveCommand("/post", out _));
    }

    [Fact]
    public void Empty_allow_list_registers_nothing()
    {
        var registry = Registry(new SlackOptions(), new FakeCommandPlugin("post"), new FakeCommandPlugin("catchup"));

        Assert.Empty(registry.RegisteredCommands);
        Assert.Equal(new[] { "catchup", "post" }, registry.SkippedCommands);
    }

    [Fact]
    public void Re_adding_a_name_re_enables_the_command()
    {
        var plugin = new FakeCommandPlugin("post");

        Assert.False(Registry(new SlackOptions(), plugin).TryResolveCommand("post", out _));
        Assert.True(Registry(SlackCommandHarness.Enabling("post"), plugin).TryResolveCommand("post", out _));
    }

    // --- Command resolution ---

    [Fact]
    public void Resolves_a_command_with_or_without_the_leading_slash()
    {
        var registry = Registry(SlackCommandHarness.Enabling("post"), new FakeCommandPlugin("post"));

        Assert.True(registry.TryResolveCommand("/post", out var withSlash));
        Assert.True(registry.TryResolveCommand("post", out var without));
        Assert.True(registry.TryResolveCommand("/POST", out var upper));
        Assert.Same(withSlash, without);
        Assert.Same(withSlash, upper);
    }

    [Fact]
    public void Allow_list_entries_tolerate_a_leading_slash()
    {
        var registry = Registry(SlackCommandHarness.Enabling("/post"), new FakeCommandPlugin("post"));

        Assert.True(registry.TryResolveCommand("/post", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/nope")]
    public void Does_not_resolve_an_unknown_or_missing_command(string? commandName)
    {
        var registry = Registry(SlackCommandHarness.Enabling("post"), new FakeCommandPlugin("post"));

        Assert.False(registry.TryResolveCommand(commandName, out _));
    }

    [Fact]
    public void Resolves_only_the_owning_plugin()
    {
        var post = new FakeCommandPlugin("post");
        var catchup = new FakeCommandPlugin("catchup");
        var registry = Registry(SlackCommandHarness.Enabling("post", "catchup"), post, catchup);

        Assert.True(registry.TryResolveCommand("post", out var resolved));
        Assert.Same(post, resolved);
        Assert.NotSame(catchup, resolved);
    }

    // --- Action resolution ---

    [Fact]
    public void Resolves_an_action_by_namespaced_id()
    {
        var post = new FakeCommandPlugin("post", "confirm", "reject");
        var registry = Registry(SlackCommandHarness.Enabling("post"), post);

        Assert.True(registry.TryResolveAction("post:confirm", out var plugin, out var actionName));
        Assert.Same(post, plugin);
        Assert.Equal("confirm", actionName);
    }

    [Fact]
    public void Two_plugins_sharing_a_bare_action_name_resolve_to_their_own_plugin()
    {
        var post = new FakeCommandPlugin("post", "confirm");
        var triage = new FakeCommandPlugin("triage", "confirm");
        var registry = Registry(SlackCommandHarness.Enabling("post", "triage"), post, triage);

        Assert.True(registry.TryResolveAction("post:confirm", out var forPost, out _));
        Assert.True(registry.TryResolveAction("triage:confirm", out var forTriage, out _));
        Assert.Same(post, forPost);
        Assert.Same(triage, forTriage);
    }

    [Fact]
    public void Does_not_resolve_an_action_whose_command_is_disabled()
    {
        // The stale-button case: a card posted while /post was enabled, clicked after it was disabled.
        var registry = Registry(new SlackOptions(), new FakeCommandPlugin("post", "confirm"));

        Assert.False(registry.TryResolveAction("post:confirm", out _, out _));
    }

    [Fact]
    public void Does_not_resolve_an_action_the_plugin_does_not_own()
    {
        var registry = Registry(SlackCommandHarness.Enabling("post"), new FakeCommandPlugin("post", "confirm"));

        Assert.False(registry.TryResolveAction("post:detonate", out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("confirm")]          // un-namespaced: a card from before namespacing
    [InlineData(":confirm")]         // empty plugin name
    [InlineData("post:")]            // empty action name
    [InlineData("unknown:confirm")]  // no such plugin
    public void Does_not_resolve_a_malformed_or_unowned_action_id(string? actionId)
    {
        var registry = Registry(SlackCommandHarness.Enabling("post"), new FakeCommandPlugin("post", "confirm"));

        Assert.False(registry.TryResolveAction(actionId, out _, out _));
    }

    // --- Action id helper ---

    [Fact]
    public void Qualify_builds_a_namespaced_id()
        => Assert.Equal("post:confirm", SlackActionId.Qualify("post", "confirm"));

    [Fact]
    public void Qualified_ids_round_trip()
    {
        Assert.True(SlackActionId.TryParse(SlackActionId.Qualify("post", "confirm"), out var plugin, out var action));
        Assert.Equal("post", plugin);
        Assert.Equal("confirm", action);
    }
}
