using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Mapping;
using SorryDave.JiraSync.Core.Slack;

namespace SorryDave.JiraSync.Tests;

/// <summary>
/// Credential-gated round-trip against the real Slack Web API. Runs only when SLACK_BOT_TOKEN is
/// set; otherwise it is a no-op. Creates a uniquely-named real channel, verifies it, then archives
/// it (Slack has no delete — archive is the cleanup).
/// </summary>
public class SlackIntegrationTests
{
    [Fact]
    public async Task Provision_against_real_slack_creates_links_and_archives_a_channel()
    {
        var token = Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
            return; // no credentials → skip

        using var http = new HttpClient { BaseAddress = new Uri("https://slack.com/api/") };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var slack = new SlackWebApiClient(http);

        using var db = new TestDb();
        var key = "SMOKE-1";
        var unique = Guid.NewGuid().ToString("N")[..8];
        db.Context.WorkItems.Add(new WorkItem
        {
            Key = key,
            ProjectKey = "SMOKE",
            IssueType = "Idea",
            Status = "To Do",
            Summary = $"integration smoke {unique}",
            JiraUpdated = DateTimeOffset.UtcNow,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            LastSyncedUtc = DateTimeOffset.UtcNow,
        });
        db.Context.SaveChanges();

        var service = new SlackChannelService(
            slack,
            new MappingStore(db.Context, TimeProvider.System),
            db.Context,
            Options.Create(new SlackOptions { BotToken = token }),
            Options.Create(new JiraOptions { BaseUrl = "https://tim-bassett.atlassian.net" }),
            NullLogger<SlackChannelService>.Instance);

        ChannelProvisionResult result;
        try
        {
            result = await service.ProvisionAsync(key);

            Assert.Equal("Created", result.Outcome);
            Assert.StartsWith("smoke-1-integration-smoke-", result.ChannelName);

            // The channel really exists in the workspace.
            var found = await slack.FindChannelByNameAsync(result.ChannelName!);
            Assert.NotNull(found);
            Assert.Equal(result.ChannelId, found!.Id);

            // The link is recorded.
            var resolved = await new MappingStore(db.NewContext(), TimeProvider.System)
                .ResolveByResourceAsync(ResourceType.SlackChannel, result.ChannelId!);
            Assert.Equal(key, resolved!.Key);
        }
        finally
        {
            // Best-effort cleanup: archive the channel we created (find it if provisioning got that far).
            var name = SlackChannelNaming.Derive(key, $"integration smoke {unique}");
            var ch = await slack.FindChannelByNameAsync(name);
            if (ch is { IsArchived: false })
                await slack.ArchiveAsync(ch.Id);
        }
    }
}
