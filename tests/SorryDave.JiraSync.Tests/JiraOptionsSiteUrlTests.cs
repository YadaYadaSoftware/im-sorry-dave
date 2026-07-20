using SorryDave.JiraSync.Core.Configuration;

namespace SorryDave.JiraSync.Tests;

/// <summary>
/// Issue links shown in Slack are built from the site URL, which is not always the REST base URL: a
/// scoped API token is rejected by the site and must be routed through the API gateway, and gateway
/// URLs are not browsable.
/// </summary>
public class JiraOptionsSiteUrlTests
{
    private const string Site = "https://elevate-digital.atlassian.net/";
    private const string Gateway = "https://api.atlassian.com/ex/jira/476b5289-969a-4113-946e-3e09c5b56d30/";

    [Fact]
    public void Site_url_defaults_to_the_base_url()
    {
        // The classic-token case: the API base is the site, so links need no separate setting.
        var options = new JiraOptions { BaseUrl = Site };

        Assert.Equal(Site, options.EffectiveSiteUrl);
    }

    [Fact]
    public void Explicit_site_url_wins_over_the_base_url()
    {
        // The scoped-token case: REST goes to the gateway, links must still point at the site.
        var options = new JiraOptions { BaseUrl = Gateway, SiteUrl = Site };

        Assert.Equal(Site, options.EffectiveSiteUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_site_url_falls_back_rather_than_producing_a_blank_link(string? siteUrl)
    {
        var options = new JiraOptions { BaseUrl = Site, SiteUrl = siteUrl };

        Assert.Equal(Site, options.EffectiveSiteUrl);
    }

    [Fact]
    public void Both_unset_yields_null_so_callers_omit_the_link()
    {
        Assert.Null(new JiraOptions().EffectiveSiteUrl);
    }
}
