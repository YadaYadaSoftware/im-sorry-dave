using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Slack.Commands;

namespace SorryDave.JiraSync.Tests;

/// <summary>A command plugin whose behaviour is supplied per-test, recording what it was asked to do.</summary>
public sealed class FakeCommandPlugin : ISlackCommandPlugin
{
    public FakeCommandPlugin(string name, params string[] actionNames)
    {
        Name = name;
        ActionNames = actionNames;
    }

    public string Name { get; }
    public string Description => $"{Name} description";
    public string AckText => $"ack:{Name}";
    public IReadOnlyCollection<string> ActionNames { get; }

    public List<SlackCommandContext> CommandCalls { get; } = new();
    public List<SlackActionContext> ActionCalls { get; } = new();

    /// <summary>Gate the handler so a test can observe the acknowledgement while work is still running.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public Exception? Throw { get; set; }

    public async Task<SlackCommandResult> HandleCommandAsync(SlackCommandContext context, CancellationToken ct = default)
    {
        lock (CommandCalls) CommandCalls.Add(context);
        if (Gate is not null) await Gate.Task;
        if (Throw is not null) throw Throw;
        return SlackCommandResult.Message($"handled:{Name}");
    }

    public Task<SlackActionResult> HandleActionAsync(SlackActionContext context, CancellationToken ct = default)
    {
        lock (ActionCalls) ActionCalls.Add(context);
        if (Throw is not null) throw Throw;
        return Task.FromResult(SlackActionResult.Message($"handled:{Name}:{context.ActionName}"));
    }
}

/// <summary>Captures what the host tried to deliver back to Slack, without any HTTP.</summary>
public sealed class RecordingResponder : ISlackResponder
{
    public List<(string Url, string Text)> Responses { get; } = new();
    public List<(string Url, string Text)> Replacements { get; } = new();

    public Task RespondAsync(string responseUrl, string text, CancellationToken ct = default)
    {
        lock (Responses) Responses.Add((responseUrl, text));
        return Task.CompletedTask;
    }

    public Task ReplaceOriginalAsync(string responseUrl, string text, object blocks, CancellationToken ct = default)
    {
        lock (Replacements) Replacements.Add((responseUrl, text));
        return Task.CompletedTask;
    }
}

/// <summary>Builds a container wired the way <c>AddJiraSyncCore</c> wires command dispatch.</summary>
public static class SlackCommandHarness
{
    public static ServiceProvider Provider(SlackOptions options, params ISlackCommandPlugin[] plugins)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(Options.Create(options));

        // Singletons so the instance the dispatcher resolves in its own background scope is the same
        // one the test asserts against.
        foreach (var plugin in plugins) services.AddSingleton(plugin);
        services.AddSingleton<ISlackResponder, RecordingResponder>();
        services.AddScoped<ISlackCommandRegistry, SlackCommandRegistry>();
        services.AddScoped<SlackCommandDispatcher>();

        return services.BuildServiceProvider();
    }

    public static SlackOptions Enabling(params string[] commands)
        => new() { EnabledCommands = commands.ToList() };

    public static Dictionary<string, string> CommandForm(
        string command, string channelId = "C1", string? responseUrl = "https://hooks.example/r", string text = "")
    {
        var form = new Dictionary<string, string>
        {
            ["command"] = command,
            ["channel_id"] = channelId,
            ["user_id"] = "U1",
            ["text"] = text,
        };
        if (responseUrl is not null) form["response_url"] = responseUrl;
        return form;
    }

    public static SlackActionContext ActionContext(string? value = "v")
        => new(ActionName: "", Value: value, UserId: "U1", ChannelId: "C1", ResponseUrl: "https://hooks.example/r");
}
