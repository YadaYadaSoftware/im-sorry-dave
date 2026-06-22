namespace SorryDave.JiraSync.SmokeTui.Api;

/// <summary>The console's view of the API. Implemented by the HTTP <see cref="ApiClient"/> and by
/// fakes in tests, so the panels and smoke runner are testable without a live server.</summary>
public interface IApiClient
{
    string BaseAddress { get; }

    Task<IReadOnlyList<WorkItemDto>> GetWorkItemsAsync(CancellationToken ct = default);
    Task<int> BackfillAsync(CancellationToken ct = default);
    Task<int> ReconcileAsync(CancellationToken ct = default);
    Task<WriteBackDto> SubmitWriteBackAsync(string workItemKey, WriteBackRequest request, CancellationToken ct = default);
    Task<int> DrainWriteBackAsync(CancellationToken ct = default);
    Task<IReadOnlyList<WriteBackDto>> GetWriteBacksAsync(CancellationToken ct = default);

    /// <summary>Post a sample issue-updated webhook for the given work item (bumps its status).</summary>
    Task SimulateWebhookAsync(WorkItemDto item, CancellationToken ct = default);

    /// <summary>Fake-mode comments, or null when the API is using a real Jira backend.</summary>
    Task<IReadOnlyList<CommentDto>?> GetFakeCommentsAsync(CancellationToken ct = default);

    /// <summary>Provision/archive/unarchive the work item's Slack channel. <paramref name="dryRun"/>
    /// reports the intended action without mutating Slack.</summary>
    Task<SlackResultDto> ProvisionChannelAsync(string workItemKey, bool dryRun, CancellationToken ct = default);
    Task<SlackResultDto> ArchiveChannelAsync(string workItemKey, bool dryRun, CancellationToken ct = default);
    Task<SlackResultDto> UnarchiveChannelAsync(string workItemKey, bool dryRun, CancellationToken ct = default);

    /// <summary>The channel name linked to the work item, or null if none.</summary>
    Task<string?> GetLinkedChannelAsync(string workItemKey, CancellationToken ct = default);

    /// <summary>Link an existing Slack channel id to the work item (rejected on conflict).</summary>
    Task<SlackResultDto> LinkChannelAsync(string workItemKey, string channelId, CancellationToken ct = default);

    /// <summary>Smoke-test summarization: extract candidates from a provided conversation (uses the
    /// real Claude extractor when the API has an Anthropic key, otherwise the fake).</summary>
    Task<SummarizeResultDto> SummarizeAsync(string workItemKey, IReadOnlyList<(string Author, string Text)> lines, CancellationToken ct = default);
    Task<string> ConfirmCandidateAsync(Guid id, CancellationToken ct = default);
    Task<string> RejectCandidateAsync(Guid id, CancellationToken ct = default);
}
