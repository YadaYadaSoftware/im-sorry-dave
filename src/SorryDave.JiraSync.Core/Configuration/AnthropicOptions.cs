namespace SorryDave.JiraSync.Core.Configuration;

/// <summary>
/// Claude (Anthropic) configuration for conversation summarization. The API key follows the platform
/// secrets convention (user-secrets locally, SSM <c>/jira-sync/Anthropic/ApiKey</c> in AWS). When the
/// key is absent, extraction uses a deterministic fake so the platform runs without Claude.
/// </summary>
public class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string? ApiKey { get; set; }

    /// <summary>Default to the most capable model; configurable down to a cheaper one for cost.</summary>
    public string Model { get; set; } = "claude-opus-4-8";

    /// <summary>Max messages handed to a single extraction call (cost/size bound).</summary>
    public int MaxWindowMessages { get; set; } = 200;

    /// <summary>Extra redaction regex patterns applied on top of the built-in set.</summary>
    public List<string> RedactionPatterns { get; set; } = new();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
