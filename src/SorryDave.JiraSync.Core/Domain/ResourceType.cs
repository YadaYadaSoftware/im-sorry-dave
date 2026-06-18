namespace SorryDave.JiraSync.Core.Domain;

/// <summary>
/// The kind of external resource that can be linked to a Jira work item.
/// Other capabilities (Slack, GitHub, OpenSpec) resolve work items through these.
/// </summary>
public enum ResourceType
{
    SlackChannel = 1,
    GitHubPullRequest = 2,
    OpenSpecChange = 3
}
