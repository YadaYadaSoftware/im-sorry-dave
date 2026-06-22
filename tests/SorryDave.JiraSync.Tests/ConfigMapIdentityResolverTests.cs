using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Slack;

namespace SorryDave.JiraSync.Tests;

public class ConfigMapIdentityResolverTests
{
    private static ConfigMapIdentityResolver Resolver(Dictionary<string, string> map)
        => new(Options.Create(new SlackOptions { UserMap = map }));

    [Fact]
    public async Task Resolves_by_account_id()
    {
        var id = await Resolver(new() { ["acc-1"] = "U111" })
            .ResolveSlackUserIdAsync(new JiraUserRef("acc-1", "Dave", null));
        Assert.Equal("U111", id);
    }

    [Fact]
    public async Task Falls_back_to_display_name()
    {
        var id = await Resolver(new() { ["Dave Bowman"] = "U222" })
            .ResolveSlackUserIdAsync(new JiraUserRef("acc-x", "Dave Bowman", null));
        Assert.Equal("U222", id);
    }

    [Fact]
    public async Task Returns_null_for_unmapped_user()
    {
        var id = await Resolver(new() { ["acc-1"] = "U111" })
            .ResolveSlackUserIdAsync(new JiraUserRef("acc-9", "Nobody", null));
        Assert.Null(id);
    }
}
