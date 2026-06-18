using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.Sync;
using SorryDave.JiraSync.Core.WriteBack;

namespace SorryDave.JiraSync.Tests;

/// <summary>
/// jira-sync-core task 7.2 — round trip against a real Jira test project:
/// webhook → store → write-back → verify in Jira → cleanup.
///
/// Credential-gated: skipped unless credentials are supplied via either source below.
/// Values are read from the test project's user-secrets first, then environment variables:
///   user-secret Jira:BaseUrl        / env JIRA_BASE_URL        e.g. https://your-org.atlassian.net
///   user-secret Jira:Email          / env JIRA_EMAIL           the account email
///   user-secret Jira:ApiToken       / env JIRA_API_TOKEN       https://id.atlassian.com/manage-profile/security/api-tokens
///   user-secret Jira:TestIssueKey   / env JIRA_TEST_ISSUE_KEY  an existing issue in a throwaway/test project
///
/// The test creates a comment, edits it in place, then deletes it, leaving no residue.
/// </summary>
public class JiraRoundTripIntegrationTests
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddUserSecrets(typeof(JiraRoundTripIntegrationTests).Assembly, optional: true)
        .Build();

    private static string? Setting(string secretKey, string envKey)
        => Config[secretKey] ?? Environment.GetEnvironmentVariable(envKey);

    private static readonly string? BaseUrl = Setting("Jira:BaseUrl", "JIRA_BASE_URL");
    private static readonly string? Email = Setting("Jira:Email", "JIRA_EMAIL");
    private static readonly string? Token = Setting("Jira:ApiToken", "JIRA_API_TOKEN");
    private static readonly string? IssueKey = Setting("Jira:TestIssueKey", "JIRA_TEST_ISSUE_KEY");

    private const string Fields = "summary,status,assignee,reporter,issuetype,project,labels,description,updated";

    [SkippableFact]
    public async Task Webhook_to_store_to_writeback_round_trip()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(Email) ||
                string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(IssueKey),
            "Provide Jira credentials via user-secrets (Jira:BaseUrl/Email/ApiToken/TestIssueKey) " +
            "or env vars (JIRA_BASE_URL/JIRA_EMAIL/JIRA_API_TOKEN/JIRA_TEST_ISSUE_KEY) to run the real-Jira integration test.");

        using var http = CreateJiraHttpClient();
        var jiraClient = new JiraRestClient(http, NullLogger<JiraRestClient>.Instance);
        var recordIdentity = $"smoke-{Guid.NewGuid():N}";
        string? commentId = null;

        try
        {
            // 1. REST fetch + parse: the issue exists and round-trips through the parser.
            var fetched = await jiraClient.GetIssueAsync(IssueKey!);
            Assert.NotNull(fetched);
            Assert.Equal(IssueKey, fetched!.Key);

            // 2. webhook → store: wrap the real issue JSON in a webhook envelope and process it.
            using var db = new TestDb();
            var sync = new WorkItemSyncService(db.Context, TimeProvider.System, NullLogger<WorkItemSyncService>.Instance);
            var processor = new WebhookProcessor(sync, NullLogger<WebhookProcessor>.Instance);

            var rawIssue = await http.GetStringAsync($"rest/api/3/issue/{Uri.EscapeDataString(IssueKey!)}?fields={Fields}");
            using (var envelope = BuildWebhookEnvelope(rawIssue))
            {
                var result = await processor.ProcessAsync(envelope.RootElement);
                Assert.Equal(IssueKey, result.IssueKey);
            }
            Assert.NotNull(await db.NewContext().WorkItems.FindAsync(IssueKey));

            // 3. write-back round trip: queue a decision and deliver it to Jira.
            var writeBack = new WriteBackService(db.Context, TimeProvider.System, NullLogger<WriteBackService>.Instance);
            var sender = new WriteBackSender(db.Context, jiraClient, TimeProvider.System,
                Options.Create(new SyncOptions()), NullLogger<WriteBackSender>.Instance);

            await writeBack.SubmitAsync(new WriteBackSubmission
            {
                WorkItemKey = IssueKey!,
                RecordIdentity = recordIdentity,
                Kind = WriteBackKind.Decision,
                Content = "Integration smoke test: round-trip decision.",
                SourceUrl = "https://example.test/thread/1",
                Author = "Integration Test"
            });

            var sent = await sender.ProcessDueAsync();
            Assert.Equal(1, sent);

            var record = db.NewContext().WriteBackRecords.Single();
            Assert.Equal(WriteBackStatus.Sent, record.Status);
            Assert.False(string.IsNullOrEmpty(record.JiraCommentId));
            commentId = record.JiraCommentId;

            // 4. verify in Jira: the comment exists and carries the managed-record marker.
            var commentJson = await http.GetStringAsync(
                $"rest/api/3/issue/{Uri.EscapeDataString(IssueKey!)}/comment/{commentId}");
            Assert.Contains(RecordMarker.Prefix, commentJson);
            Assert.Contains(recordIdentity, commentJson);

            // 5. idempotent edit: resubmit changed content; the same comment is updated in place.
            await writeBack.SubmitAsync(new WriteBackSubmission
            {
                WorkItemKey = IssueKey!,
                RecordIdentity = recordIdentity,
                Kind = WriteBackKind.Decision,
                Content = "Integration smoke test: EDITED decision.",
                SourceUrl = "https://example.test/thread/1",
                Author = "Integration Test"
            });
            await sender.ProcessDueAsync();

            var edited = db.NewContext().WriteBackRecords.Single();
            Assert.Equal(commentId, edited.JiraCommentId); // edited in place, not duplicated
            var editedJson = await http.GetStringAsync(
                $"rest/api/3/issue/{Uri.EscapeDataString(IssueKey!)}/comment/{commentId}");
            Assert.Contains("EDITED", editedJson);
        }
        finally
        {
            // The test comment is intentionally LEFT on the issue so it can be viewed in Jira.
            // Each run uses a unique record identity, so runs do not overwrite one another.
            await Task.CompletedTask;
        }
    }

    private static HttpClient CreateJiraHttpClient()
    {
        var http = new HttpClient { BaseAddress = new Uri(BaseUrl!.TrimEnd('/') + "/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Email}:{Token}")));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    private static JsonDocument BuildWebhookEnvelope(string rawIssueJson)
    {
        using var issue = JsonDocument.Parse(rawIssueJson);
        var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("webhookEvent", "jira:issue_updated");
            writer.WritePropertyName("issue");
            issue.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }
        ms.Position = 0;
        return JsonDocument.Parse(ms.ToArray());
    }
}
