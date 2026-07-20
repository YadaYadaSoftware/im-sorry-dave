namespace SorryDave.JiraSync.Core.Configuration;

public class JiraOptions
{
    public const string SectionName = "Jira";

    /// <summary>
    /// Base URL the REST client calls. For a classic API token this is the site itself
    /// (<c>https://your-org.atlassian.net</c>). A <b>scoped</b> API token is rejected by the site and
    /// must go through the API gateway instead:
    /// <c>https://api.atlassian.com/ex/jira/{cloudId}</c> — where the cloud id comes from
    /// <c>https://your-org.atlassian.net/_edge/tenant_info</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The human-facing site URL used to build issue links (<c>{SiteUrl}/browse/{KEY}</c>) shown in
    /// Slack. Defaults to <see cref="BaseUrl"/>, which is correct whenever the API base <em>is</em> the
    /// site. It must be set explicitly when <see cref="BaseUrl"/> points at the API gateway, since
    /// gateway URLs are not browsable — links built from one would 404 for every user.
    /// </summary>
    public string? SiteUrl { get; set; }

    /// <summary>Site URL for browse links, falling back to <see cref="BaseUrl"/>.</summary>
    public string? EffectiveSiteUrl => string.IsNullOrWhiteSpace(SiteUrl) ? BaseUrl : SiteUrl;

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
