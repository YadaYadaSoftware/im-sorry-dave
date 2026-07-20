using Microsoft.Extensions.DependencyInjection;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Slack.Commands;

namespace SorryDave.JiraSync.Tests;

public class SlackCommandDispatcherTests
{
    // --- Command dispatch ---

    [Fact]
    public async Task Dispatches_to_the_owning_plugin_and_no_other()
    {
        var post = new FakeCommandPlugin("post");
        var catchup = new FakeCommandPlugin("catchup");
        await using var provider = SlackCommandHarness.Provider(
            SlackCommandHarness.Enabling("post", "catchup"), post, catchup);
        var dispatcher = provider.GetRequiredService<SlackCommandDispatcher>();

        var dispatch = dispatcher.DispatchCommand(SlackCommandHarness.CommandForm("/post"));
        await dispatch.Completion;

        Assert.True(dispatch.Accepted);
        Assert.Single(post.CommandCalls);
        Assert.Empty(catchup.CommandCalls);
    }

    [Fact]
    public async Task Passes_the_invocation_details_to_the_plugin()
    {
        var post = new FakeCommandPlugin("post");
        await using var provider = SlackCommandHarness.Provider(SlackCommandHarness.Enabling("post"), post);

        var form = SlackCommandHarness.CommandForm("/post", channelId: "C42", text: "since friday");
        await provider.GetRequiredService<SlackCommandDispatcher>().DispatchCommand(form).Completion;

        var context = Assert.Single(post.CommandCalls);
        Assert.Equal("post", context.CommandName);
        Assert.Equal("C42", context.ChannelId);
        Assert.Equal("since friday", context.Text);
        Assert.Equal("U1", context.UserId);
    }

    [Fact]
    public async Task Refuses_a_disabled_command_without_invoking_the_handler()
    {
        var post = new FakeCommandPlugin("post");
        await using var provider = SlackCommandHarness.Provider(new SlackOptions(), post);

        var dispatch = provider.GetRequiredService<SlackCommandDispatcher>()
            .DispatchCommand(SlackCommandHarness.CommandForm("/post"));
        await dispatch.Completion;

        Assert.False(dispatch.Accepted);
        Assert.Equal(SlackCommandDispatcher.UnavailableCommandText, dispatch.AckText);
        Assert.Empty(post.CommandCalls);
    }

    [Fact]
    public async Task Refuses_an_unowned_command()
    {
        await using var provider = SlackCommandHarness.Provider(
            SlackCommandHarness.Enabling("post"), new FakeCommandPlugin("post"));

        var dispatch = provider.GetRequiredService<SlackCommandDispatcher>()
            .DispatchCommand(SlackCommandHarness.CommandForm("/nope"));
        await dispatch.Completion;

        Assert.False(dispatch.Accepted);
        Assert.Equal(SlackCommandDispatcher.UnavailableCommandText, dispatch.AckText);
    }

    // --- Acknowledgement and background execution ---

    [Fact]
    public async Task Acknowledges_with_the_plugin_text_before_the_handler_completes()
    {
        var post = new FakeCommandPlugin("post") { Gate = new TaskCompletionSource() };
        await using var provider = SlackCommandHarness.Provider(SlackCommandHarness.Enabling("post"), post);

        var dispatch = provider.GetRequiredService<SlackCommandDispatcher>()
            .DispatchCommand(SlackCommandHarness.CommandForm("/post"));

        // The ack is available while the handler is still blocked on its gate.
        Assert.Equal("ack:post", dispatch.AckText);
        Assert.False(dispatch.Completion.IsCompleted);

        post.Gate.SetResult();
        await dispatch.Completion;
        Assert.True(dispatch.Completion.IsCompleted);
    }

    [Fact]
    public async Task A_handler_slower_than_the_ack_still_delivers_via_the_response_url()
    {
        var post = new FakeCommandPlugin("post") { Gate = new TaskCompletionSource() };
        await using var provider = SlackCommandHarness.Provider(SlackCommandHarness.Enabling("post"), post);
        var responder = (RecordingResponder)provider.GetRequiredService<ISlackResponder>();

        var dispatch = provider.GetRequiredService<SlackCommandDispatcher>()
            .DispatchCommand(SlackCommandHarness.CommandForm("/post"));

        Assert.Empty(responder.Responses);   // nothing delivered yet — the handler is still running
        post.Gate.SetResult();
        await dispatch.Completion;

        var (url, text) = Assert.Single(responder.Responses);
        Assert.Equal("https://hooks.example/r", url);
        Assert.Equal("handled:post", text);
    }

    [Fact]
    public async Task The_handler_survives_disposal_of_the_scope_that_dispatched_it()
    {
        // The footgun this design exists to remove: the request's scope is disposed the moment we ack,
        // so the handler must run in a scope the dispatcher owns.
        var post = new FakeCommandPlugin("post") { Gate = new TaskCompletionSource() };
        await using var provider = SlackCommandHarness.Provider(SlackCommandHarness.Enabling("post"), post);
        var responder = (RecordingResponder)provider.GetRequiredService<ISlackResponder>();

        SlackDispatchResult dispatch;
        using (var requestScope = provider.CreateScope())
        {
            dispatch = requestScope.ServiceProvider.GetRequiredService<SlackCommandDispatcher>()
                .DispatchCommand(SlackCommandHarness.CommandForm("/post"));
        }   // request scope disposed here, before the handler has done anything

        post.Gate.SetResult();
        await dispatch.Completion;

        Assert.Single(post.CommandCalls);
        Assert.Equal("handled:post", Assert.Single(responder.Responses).Text);
    }

    [Fact]
    public async Task A_throwing_handler_reports_a_failure_instead_of_surfacing()
    {
        var post = new FakeCommandPlugin("post") { Throw = new InvalidOperationException("boom") };
        await using var provider = SlackCommandHarness.Provider(SlackCommandHarness.Enabling("post"), post);
        var responder = (RecordingResponder)provider.GetRequiredService<ISlackResponder>();

        var dispatch = provider.GetRequiredService<SlackCommandDispatcher>()
            .DispatchCommand(SlackCommandHarness.CommandForm("/post"));
        await dispatch.Completion;   // must not throw

        Assert.Contains("failed", Assert.Single(responder.Responses).Text);
    }

    [Fact]
    public async Task Delivers_nothing_when_slack_supplied_no_response_url()
    {
        var post = new FakeCommandPlugin("post");
        await using var provider = SlackCommandHarness.Provider(SlackCommandHarness.Enabling("post"), post);
        var responder = (RecordingResponder)provider.GetRequiredService<ISlackResponder>();

        var form = SlackCommandHarness.CommandForm("/post", responseUrl: null);
        await provider.GetRequiredService<SlackCommandDispatcher>().DispatchCommand(form).Completion;

        Assert.Single(post.CommandCalls);
        Assert.Empty(responder.Responses);
    }

    // --- Action dispatch ---

    [Fact]
    public async Task Dispatches_an_action_to_its_owning_plugin()
    {
        var post = new FakeCommandPlugin("post", "confirm", "reject");
        var triage = new FakeCommandPlugin("triage", "confirm");
        await using var provider = SlackCommandHarness.Provider(
            SlackCommandHarness.Enabling("post", "triage"), post, triage);

        var result = await provider.GetRequiredService<SlackCommandDispatcher>()
            .DispatchActionAsync("post:confirm", SlackCommandHarness.ActionContext());

        Assert.Equal("handled:post:confirm", result.Headline);
        Assert.Equal("confirm", Assert.Single(post.ActionCalls).ActionName);
        Assert.Empty(triage.ActionCalls);
    }

    [Fact]
    public async Task Refuses_an_action_whose_command_is_disabled()
    {
        var post = new FakeCommandPlugin("post", "confirm");
        await using var provider = SlackCommandHarness.Provider(new SlackOptions(), post);

        var result = await provider.GetRequiredService<SlackCommandDispatcher>()
            .DispatchActionAsync("post:confirm", SlackCommandHarness.ActionContext());

        Assert.Equal(SlackCommandDispatcher.UnavailableActionText, result.Headline);
        Assert.Empty(post.ActionCalls);
    }

    [Theory]
    [InlineData("confirm")]          // un-namespaced, from a card posted before namespacing
    [InlineData("unknown:confirm")]
    [InlineData(null)]
    public async Task Refuses_an_unrecognized_action_id_without_invoking_anything(string? actionId)
    {
        var post = new FakeCommandPlugin("post", "confirm");
        await using var provider = SlackCommandHarness.Provider(SlackCommandHarness.Enabling("post"), post);

        var result = await provider.GetRequiredService<SlackCommandDispatcher>()
            .DispatchActionAsync(actionId, SlackCommandHarness.ActionContext());

        Assert.Equal(SlackCommandDispatcher.UnavailableActionText, result.Headline);
        Assert.Empty(post.ActionCalls);
    }

    [Fact]
    public async Task A_throwing_action_handler_reports_a_failure()
    {
        var post = new FakeCommandPlugin("post", "confirm") { Throw = new InvalidOperationException("boom") };
        await using var provider = SlackCommandHarness.Provider(SlackCommandHarness.Enabling("post"), post);

        var result = await provider.GetRequiredService<SlackCommandDispatcher>()
            .DispatchActionAsync("post:confirm", SlackCommandHarness.ActionContext());

        Assert.Contains("failed", result.Headline);
    }
}
