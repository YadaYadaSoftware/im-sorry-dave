namespace SorryDave.JiraSync.Core.Configuration;

public class JiraOptions
{
    public const string SectionName = "Jira";

    /// <summary>Base URL of the Jira Cloud site, e.g. https://your-org.atlassian.net.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Account email used for basic auth with the API token.</summary>
    public string? Email { get; set; }

    /// <summary>Jira API token (store via user-secrets / a secret store, never in source).</summary>
    public string? ApiToken { get; set; }

    /// <summary>Project keys whose issues are tracked. Used to scope reconciliation/backfill.</summary>
    public List<string> ProjectKeys { get; set; } = new();

    /// <summary>Optional extra JQL appended to the tracked-issue filter.</summary>
    public string? AdditionalJql { get; set; }

    /// <summary>
    /// Force the in-memory fake client even if credentials are present. When unset, the
    /// fake client is used automatically whenever <see cref="BaseUrl"/>/<see cref="ApiToken"/>
    /// are missing, so the service is reviewable locally with no Jira account.
    /// </summary>
    public bool? UseFake { get; set; }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(ApiToken);
}
