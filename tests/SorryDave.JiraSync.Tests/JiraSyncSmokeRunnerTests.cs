using SorryDave.JiraSync.SmokeTui.Api;
using SorryDave.JiraSync.SmokeTui.Smoke;

namespace SorryDave.JiraSync.Tests;

public class JiraSyncSmokeRunnerTests
{
    [Fact]
    public async Task Guided_run_passes_every_step_against_a_fake_api()
    {
        var steps = await new JiraSyncSmokeRunner(new FakeApiClient()).RunAsync();

        Assert.NotEmpty(steps);
        Assert.True(JiraSyncSmokeRunner.AllPassed(steps),
            "Steps:\n" + string.Join("\n", steps.Select(s => $"[{(s.Passed ? "PASS" : "FAIL")}] {s.Name} — {s.Detail}")));
        Assert.Contains(steps, s => s.Name == "Verify delivery" && s.Passed);
    }

    /// <summary>In-memory IApiClient that mimics the API's happy-path behavior.</summary>
    private sealed class FakeApiClient : IApiClient
    {
        private readonly List<WorkItemDto> _items = new();
        private readonly List<WriteBackDto> _writeBacks = new();
        private int _commentSeq;

        public string BaseAddress => "fake://api";

        public Task<int> BackfillAsync(CancellationToken ct = default)
        {
            if (_items.Count == 0)
                _items.Add(new WorkItemDto("DAVE-1", "DAVE", "Story", "To Do", "Dave Bowman", "Open the pod bay doors", false));
            return Task.FromResult(_items.Count);
        }

        public Task<int> ReconcileAsync(CancellationToken ct = default) => Task.FromResult(_items.Count);

        public Task<IReadOnlyList<WorkItemDto>> GetWorkItemsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WorkItemDto>>(_items.ToList());

        public Task<WriteBackDto> SubmitWriteBackAsync(string workItemKey, WriteBackRequest request, CancellationToken ct = default)
        {
            var record = new WriteBackDto(Guid.NewGuid(), workItemKey, request.RecordIdentity, request.Kind, "Pending", null, 0, null);
            _writeBacks.Add(record);
            return Task.FromResult(record);
        }

        public Task<int> DrainWriteBackAsync(CancellationToken ct = default)
        {
            var drained = 0;
            for (var i = 0; i < _writeBacks.Count; i++)
            {
                if (_writeBacks[i].Status == "Pending")
                {
                    _writeBacks[i] = _writeBacks[i] with { Status = "Sent", JiraCommentId = $"c{++_commentSeq}" };
                    drained++;
                }
            }
            return Task.FromResult(drained);
        }

        public Task<IReadOnlyList<WriteBackDto>> GetWriteBacksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WriteBackDto>>(_writeBacks.ToList());

        public Task SimulateWebhookAsync(WorkItemDto item, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<CommentDto>?> GetFakeCommentsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CommentDto>?>(new List<CommentDto>());

        public Task<SlackResultDto> ProvisionChannelAsync(string workItemKey, bool dryRun, CancellationToken ct = default)
            => Task.FromResult(new SlackResultDto("NotConfigured", null, null, null));
        public Task<SlackResultDto> ArchiveChannelAsync(string workItemKey, bool dryRun, CancellationToken ct = default)
            => Task.FromResult(new SlackResultDto("NotConfigured", null, null, null));
        public Task<SlackResultDto> UnarchiveChannelAsync(string workItemKey, bool dryRun, CancellationToken ct = default)
            => Task.FromResult(new SlackResultDto("NotConfigured", null, null, null));
        public Task<string?> GetLinkedChannelAsync(string workItemKey, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public Task<SlackResultDto> LinkChannelAsync(string workItemKey, string channelId, CancellationToken ct = default)
            => Task.FromResult(new SlackResultDto("Linked", channelId, null, null));
        public Task<SummarizeResultDto> SummarizeAsync(string workItemKey, IReadOnlyList<(string Author, string Text)> lines, CancellationToken ct = default)
            => Task.FromResult(new SummarizeResultDto("Extracted", null, new()));
        public Task<string> ConfirmCandidateAsync(Guid id, CancellationToken ct = default) => Task.FromResult("Confirmed");
        public Task<string> RejectCandidateAsync(Guid id, CancellationToken ct = default) => Task.FromResult("Rejected");
    }
}
