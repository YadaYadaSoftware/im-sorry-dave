using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SorryDave.JiraSync.Core.Slack.Commands;

namespace SorryDave.JiraSync.Tests;

public class SlackResponderTests
{
    private const string Url = "https://hooks.example/r";

    private static (SlackResponder Responder, RecordingHandler Handler) Build(
        HttpStatusCode status = HttpStatusCode.OK, bool throws = false)
    {
        var handler = new RecordingHandler { Status = status, Throws = throws };
        var services = new ServiceCollection();
        // SlackResponder calls CreateClient() with no name, so configure the default client.
        services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => handler);
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return (new SlackResponder(factory), handler);
    }

    [Fact]
    public async Task Responds_with_an_ephemeral_message()
    {
        var (responder, handler) = Build();

        await responder.RespondAsync(Url, "hello");

        Assert.Equal(Url, handler.LastUri);
        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("ephemeral", doc.RootElement.GetProperty("response_type").GetString());
        Assert.Equal("hello", doc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Replacing_the_original_carries_the_replacement_flag_and_blocks()
    {
        var (responder, handler) = Build();

        await responder.ReplaceOriginalAsync(Url, "done", new[] { new { type = "section" } });

        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.True(doc.RootElement.GetProperty("replace_original").GetBoolean());
        Assert.Equal("done", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("blocks").ValueKind);
    }

    [Fact]
    public async Task Delivery_failures_are_swallowed()
    {
        // The plugin's work has already happened by the time we report on it, so a failed report must
        // never surface as a handler failure.
        var (responder, _) = Build(throws: true);

        await responder.RespondAsync(Url, "hello");
        await responder.ReplaceOriginalAsync(Url, "done", Array.Empty<object>());
    }

    [Fact]
    public async Task A_non_success_status_is_not_treated_as_an_error()
    {
        var (responder, _) = Build(HttpStatusCode.InternalServerError);

        await responder.RespondAsync(Url, "hello");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool Throws { get; set; }
        public string? LastUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Throws) throw new HttpRequestException("network down");
            LastUri = request.RequestUri?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status);
        }
    }
}
