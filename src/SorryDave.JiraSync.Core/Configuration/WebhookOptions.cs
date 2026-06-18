namespace SorryDave.JiraSync.Core.Configuration;

public class WebhookOptions
{
    public const string SectionName = "Webhook";

    /// <summary>
    /// Shared secret required on inbound Jira webhook requests (header <c>X-Webhook-Secret</c>
    /// or query <c>?secret=</c>). When empty, the secret check is skipped — convenient for
    /// local review, but a secret SHOULD be configured in any shared environment.
    /// </summary>
    public string? Secret { get; set; }
}
