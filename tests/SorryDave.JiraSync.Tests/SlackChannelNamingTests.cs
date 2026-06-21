using SorryDave.JiraSync.Core.Slack;

namespace SorryDave.JiraSync.Tests;

public class SlackChannelNamingTests
{
    [Fact]
    public void Derives_key_plus_summary_slug()
        => Assert.Equal("mdp-7-build-slack-channel", SlackChannelNaming.Derive("MDP-7", "Build Slack Channel"));

    [Fact]
    public void Collapses_nonalphanumeric_runs_to_single_hyphens()
        => Assert.Equal("mdp-7-re-do-the-api", SlackChannelNaming.Derive("MDP-7", "Re-do  the  API!!"));

    [Fact]
    public void Empty_or_null_summary_yields_just_the_key()
    {
        Assert.Equal("mdp-7", SlackChannelNaming.Derive("MDP-7", ""));
        Assert.Equal("mdp-7", SlackChannelNaming.Derive("MDP-7", null));
    }

    [Fact]
    public void Appends_deterministic_collision_suffix()
        => Assert.Equal("mdp-7-build-slack-channel-2", SlackChannelNaming.Derive("MDP-7", "Build Slack Channel", "2"));

    [Fact]
    public void Truncates_long_summary_keeping_key_and_under_limit()
    {
        var name = SlackChannelNaming.Derive("MDP-7", new string('a', 200));

        Assert.True(name.Length <= SlackChannelNaming.MaxLength, $"length {name.Length}");
        Assert.StartsWith("mdp-7-", name);
        Assert.DoesNotContain("--", name);
        Assert.False(name.EndsWith('-'));
    }

    [Fact]
    public void Truncation_leaves_room_for_the_collision_suffix()
    {
        var name = SlackChannelNaming.Derive("MDP-7", new string('a', 200), "12");

        Assert.True(name.Length <= SlackChannelNaming.MaxLength, $"length {name.Length}");
        Assert.EndsWith("-12", name);
        Assert.StartsWith("mdp-7-", name);
    }
}
