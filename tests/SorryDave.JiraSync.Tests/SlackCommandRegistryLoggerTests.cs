using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Slack.Commands;

namespace SorryDave.JiraSync.Tests;

/// <summary>
/// The allow-list fails closed, so a command missing from production looks exactly like one that was
/// never built. Startup logging is what makes the difference diagnosable.
/// </summary>
public class SlackCommandRegistryLoggerTests
{
    private static async Task<List<(LogLevel Level, string Message)>> Run(
        SlackOptions options, params ISlackCommandPlugin[] plugins)
    {
        var sink = new ListLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => { b.AddProvider(sink); b.SetMinimumLevel(LogLevel.Trace); });
        services.AddSingleton(Options.Create(options));
        foreach (var plugin in plugins) services.AddSingleton(plugin);
        services.AddScoped<ISlackCommandRegistry, SlackCommandRegistry>();

        await using var provider = services.BuildServiceProvider();
        var logger = new SlackCommandRegistryLogger(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<SlackCommandRegistryLogger>>());

        await logger.StartAsync(CancellationToken.None);
        await logger.StopAsync(CancellationToken.None);
        return sink.Entries;
    }

    [Fact]
    public async Task Reports_the_registered_commands()
    {
        var entries = await Run(SlackCommandHarness.Enabling("post"), new FakeCommandPlugin("post"));

        Assert.Contains(entries, e => e.Level == LogLevel.Information && e.Message.Contains("/post"));
    }

    [Fact]
    public async Task Warns_when_nothing_is_registered_and_names_what_was_skipped()
    {
        var entries = await Run(new SlackOptions(), new FakeCommandPlugin("post"));

        var warning = Assert.Single(entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("/post", warning.Message);
        Assert.Contains("Slack:EnabledCommands", warning.Message);
    }

    [Fact]
    public async Task Warns_distinctly_when_no_plugins_exist_at_all()
    {
        var entries = await Run(new SlackOptions());

        Assert.Contains(entries, e => e.Level == LogLevel.Warning && e.Message.Contains("(none found)"));
    }

    [Fact]
    public async Task Reports_skipped_commands_alongside_registered_ones()
    {
        var entries = await Run(
            SlackCommandHarness.Enabling("post"), new FakeCommandPlugin("post"), new FakeCommandPlugin("catchup"));

        Assert.Contains(entries, e => e.Message.Contains("registered") && e.Message.Contains("/post"));
        Assert.Contains(entries, e => e.Message.Contains("skipped") && e.Message.Contains("/catchup"));
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new ListLogger(Entries);
        public void Dispose() { }

        private sealed class ListLogger : ILogger
        {
            private readonly List<(LogLevel, string)> _entries;
            public ListLogger(List<(LogLevel, string)> entries) => _entries = entries;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
