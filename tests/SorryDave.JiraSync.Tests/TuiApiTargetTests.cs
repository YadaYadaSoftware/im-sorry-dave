using System.Net;
using Microsoft.Extensions.Configuration;
using SorryDave.JiraSync.SmokeTui;
using SorryDave.JiraSync.SmokeTui.Api;

namespace SorryDave.JiraSync.Tests;

public class TuiApiTargetTests
{
    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> TwoTargets() => new()
    {
        ["ApiTargets:local:BaseUrl"] = "http://localhost:5050",
        ["ApiTargets:aws:BaseUrl"] = "https://jsg.appcloud.systems",
        ["ApiTargets:aws:WebhookSecret"] = "shh",
        ["ActiveApiTarget"] = "local",
    };

    [Fact]
    public void Resolves_configured_default_and_binds_secret()
    {
        var resolved = AppServices.Resolve(Config(TwoTargets()));

        Assert.Equal("local", resolved.ActiveName);
        Assert.Equal(new[] { "aws", "local" }, resolved.Targets.Keys.OrderBy(k => k));
        Assert.Equal("shh", resolved.Targets["aws"].WebhookSecret);
        Assert.Null(resolved.Targets["local"].WebhookSecret);
    }

    [Fact]
    public void Command_line_target_overrides_default()
    {
        var values = TwoTargets();
        values["target"] = "aws"; // AddCommandLine maps --target aws to this key

        Assert.Equal("aws", AppServices.Resolve(Config(values)).ActiveName);
    }

    [Fact]
    public void Aspire_injected_endpoint_folds_into_local_and_is_the_default()
    {
        var values = TwoTargets();
        values["services:api:http:0"] = "http://127.0.0.1:7777";

        var resolved = AppServices.Resolve(Config(values));

        Assert.Equal("local", resolved.ActiveName);                 // launched by the AppHost → local is the running API
        Assert.Equal("http://127.0.0.1:7777", resolved.Targets["local"].BaseUrl);
        Assert.False(resolved.Targets.ContainsKey("aspire"));       // no separate/confusing entry
    }

    [Fact]
    public void Command_line_target_overrides_even_when_injected()
    {
        var values = TwoTargets();
        values["services:api:http:0"] = "http://127.0.0.1:7777";
        values["target"] = "aws";

        Assert.Equal("aws", AppServices.Resolve(Config(values)).ActiveName);
    }

    [Fact]
    public void Falls_back_to_localhost_when_nothing_configured()
    {
        var resolved = AppServices.Resolve(Config(new Dictionary<string, string?>()));

        Assert.Equal("local", resolved.ActiveName);
        Assert.Equal("http://localhost:5050", resolved.Active.BaseUrl);
    }

    [Fact]
    public async Task Webhook_call_includes_secret_when_configured_and_omits_it_otherwise()
    {
        var item = new WorkItemDto("MDP-7", "MDP", "Idea", "To Do", "Dave", "Build", false);

        var secured = new CapturingHandler();
        await new ApiClient(new HttpClient(secured) { BaseAddress = new Uri("http://localhost:5050/") }, "topsecret")
            .SimulateWebhookAsync(item);
        Assert.Contains("secret=topsecret", secured.LastUri!.Query);

        var unsecured = new CapturingHandler();
        await new ApiClient(new HttpClient(unsecured) { BaseAddress = new Uri("http://localhost:5050/") })
            .SimulateWebhookAsync(item);
        Assert.DoesNotContain("secret=", unsecured.LastUri!.Query);
        Assert.EndsWith("/webhooks/jira", unsecured.LastUri!.AbsolutePath);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
