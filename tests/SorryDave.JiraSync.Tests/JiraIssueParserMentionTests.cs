using System.Text.Json;
using SorryDave.JiraSync.Core.Jira;

namespace SorryDave.JiraSync.Tests;

public class JiraIssueParserMentionTests
{
    [Fact]
    public void Extracts_reporter_account_id_and_description_mentions()
    {
        var json = """
        {
          "key": "MDP-9",
          "fields": {
            "project": { "key": "MDP" },
            "issuetype": { "name": "Idea" },
            "status": { "name": "To Do" },
            "summary": "Mention test",
            "reporter": { "accountId": "acc-reporter", "displayName": "Tim Bassett" },
            "updated": "2026-06-22T00:00:00.000+0000",
            "description": {
              "type": "doc", "version": 1,
              "content": [
                { "type": "paragraph", "content": [
                  { "type": "text", "text": "cc " },
                  { "type": "mention", "attrs": { "id": "acc-alice", "text": "@Alice" } },
                  { "type": "text", "text": " and " },
                  { "type": "mention", "attrs": { "id": "acc-bob", "text": "@Bob" } }
                ] }
              ]
            }
          }
        }
        """;

        var data = JiraIssueParser.Parse(JsonDocument.Parse(json).RootElement);

        Assert.Equal("acc-reporter", data.ReporterAccountId);
        Assert.Equal("Tim Bassett", data.ReporterDisplayName);
        Assert.Equal(new[] { "acc-alice", "acc-bob" }, data.MentionedAccountIds);
    }

    [Fact]
    public void No_mentions_yields_empty_list_and_plain_string_description_is_safe()
    {
        var json = """
        {
          "key": "MDP-10",
          "fields": {
            "summary": "Plain",
            "description": "just text, no mentions",
            "updated": "2026-06-22T00:00:00.000+0000"
          }
        }
        """;

        var data = JiraIssueParser.Parse(JsonDocument.Parse(json).RootElement);

        Assert.Empty(data.MentionedAccountIds);
        Assert.Null(data.ReporterAccountId);
        Assert.Equal("just text, no mentions", data.Description);
    }
}
