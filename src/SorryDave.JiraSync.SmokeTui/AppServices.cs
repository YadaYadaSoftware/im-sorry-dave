using Microsoft.Extensions.Hosting;
using SorryDave.JiraSync.SmokeTui.Api;

namespace SorryDave.JiraSync.SmokeTui;

/// <summary>
/// Resolves the API the console talks to. The base address comes from configuration: Aspire
/// injects it via service discovery (<c>services:api:http:0</c>); otherwise an explicit
/// <c>ApiBaseUrl</c> or a localhost default is used for standalone runs.
/// </summary>
public static class AppServices
{
    public static (IApiClient Client, string BaseUrl) Build(string[] args)
    {
        var config = Host.CreateApplicationBuilder(args).Configuration;

        var baseUrl = config["services:api:http:0"]
                      ?? config["services:api:https:0"]
                      ?? config["ApiBaseUrl"]
                      ?? "http://localhost:5050";

        var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        return (new ApiClient(http), baseUrl);
    }
}
