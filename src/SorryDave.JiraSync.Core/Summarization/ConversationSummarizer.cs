using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SorryDave.JiraSync.Core.Configuration;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Mapping;
using SorryDave.JiraSync.Core.Persistence;
using SorryDave.JiraSync.Core.Slack;
using SorryDave.JiraSync.Core.WriteBack;

namespace SorryDave.JiraSync.Core.Summarization;

/// <summary>
/// Captures conversation from linked channels and runs the <c>/post</c> loop: window since the last
/// successful post → redact → extract → candidates → confirm → idempotent write-back → advance cursor.
/// </summary>
public sealed class ConversationSummarizer : IConversationSummarizer
{
    private readonly JiraSyncDbContext _db;
    private readonly IMappingStore _mappings;
    private readonly IDecisionExtractor _extractor;
    private readonly IWriteBackService _writeBack;
    private readonly Redactor _redactor;
    private readonly AnthropicOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ConversationSummarizer> _logger;

    public ConversationSummarizer(
        JiraSyncDbContext db, IMappingStore mappings, IDecisionExtractor extractor,
        IWriteBackService writeBack, Redactor redactor, IOptions<AnthropicOptions> options,
        TimeProvider clock, ILogger<ConversationSummarizer> logger)
    {
        _db = db;
        _mappings = mappings;
        _extractor = extractor;
        _writeBack = writeBack;
        _redactor = redactor;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task CaptureAsync(IncomingMessage message, CancellationToken ct = default)
    {
        var workItem = await _mappings.ResolveByResourceAsync(ResourceType.SlackChannel, message.ChannelId, ct);
        if (workItem is null) return; // unlinked channel — ignore

        var existing = await _db.CapturedMessages
            .FirstOrDefaultAsync(m => m.ChannelId == message.ChannelId && m.Ts == message.Ts, ct);

        if (existing is null)
        {
            _db.CapturedMessages.Add(new CapturedMessage
            {
                ChannelId = message.ChannelId,
                WorkItemKey = workItem.Key,
                Ts = message.Ts,
                ThreadTs = message.ThreadTs,
                AuthorId = message.AuthorId,
                Text = _redactor.Redact(message.Text),
                IsDeleted = message.Deleted,
                PostedUtc = TsToUtc(message.Ts),
                CapturedUtc = _clock.GetUtcNow(),
            });
        }
        else
        {
            // Edit/delete updates to a stored message.
            existing.Text = _redactor.Redact(message.Text);
            existing.IsDeleted = message.Deleted;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<PostResult> PostAsync(string channelId, CancellationToken ct = default)
    {
        var workItem = await _mappings.ResolveByResourceAsync(ResourceType.SlackChannel, channelId, ct);
        if (workItem is null)
            return new("NotLinked", Array.Empty<SummaryCandidate>(), "This channel is not linked to a work item.");

        var cursor = await _db.PostCursors.FirstOrDefaultAsync(c => c.ChannelId == channelId, ct);
        var since = cursor?.LastPostedTs;

        // Window = messages since the last successful /post (whole conversation on first post).
        var query = _db.CapturedMessages
            .Where(m => m.ChannelId == channelId && !m.IsDeleted);
        if (since is not null)
            query = query.Where(m => string.Compare(m.Ts, since) > 0);
        var window = await query.OrderBy(m => m.Ts).Take(_options.MaxWindowMessages).ToListAsync(ct);

        if (window.Count == 0)
            return new("Empty", Array.Empty<SummaryCandidate>(), "No new conversation since the last post.");

        var lines = window
            .Select(m => new TranscriptLine(m.AuthorId ?? "unknown", _redactor.Redact(m.Text), m.Ts))
            .ToList();

        var extracted = await _extractor.ExtractAsync(workItem.Key, lines, ct);

        var fromTs = window[0].Ts;
        var toTs = window[^1].Ts;
        var candidates = extracted.Select(e => new SummaryCandidate
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            WorkItemKey = workItem.Key,
            Kind = e.Kind,
            Content = _redactor.Redact(e.Content),
            Evidence = _redactor.Redact(e.Evidence ?? ""),
            Confidence = e.Confidence,
            // Stable identity per (channel, window-end, kind, content) so re-confirm never duplicates.
            RecordIdentity = $"slack:{channelId}:{toTs}:{(int)e.Kind}:{StableHash(e.Content)}",
            Status = CandidateStatus.Pending,
            WindowFromTs = fromTs,
            WindowToTs = toTs,
            CreatedUtc = _clock.GetUtcNow(),
        }).ToList();

        _db.SummaryCandidates.AddRange(candidates);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("/post on {Channel} ({Key}): {Count} candidate(s) over {N} message(s).",
            channelId, workItem.Key, candidates.Count, window.Count);
        return new("Extracted", candidates);
    }

    public async Task<PostResult> SmokeSummarizeAsync(string workItemKey, IReadOnlyList<TranscriptLine> lines, CancellationToken ct = default)
    {
        var item = await _db.WorkItems.FirstOrDefaultAsync(w => w.Key == workItemKey, ct);
        if (item is null)
            return new("NotFound", Array.Empty<SummaryCandidate>(), $"Work item {workItemKey} not found.");
        if (lines.Count == 0)
            return new("Empty", Array.Empty<SummaryCandidate>(), "No conversation provided.");

        var redacted = lines.Select(l => l with { Text = _redactor.Redact(l.Text) }).ToList();
        var extracted = await _extractor.ExtractAsync(workItemKey, redacted, ct);

        var candidates = extracted.Select(e => new SummaryCandidate
        {
            Id = Guid.NewGuid(),
            ChannelId = $"smoke:{workItemKey}",
            WorkItemKey = workItemKey,
            Kind = e.Kind,
            Content = _redactor.Redact(e.Content),
            Evidence = _redactor.Redact(e.Evidence ?? ""),
            Confidence = e.Confidence,
            RecordIdentity = $"smoke:{workItemKey}:{(int)e.Kind}:{StableHash(e.Content)}",
            Status = CandidateStatus.Pending,
            WindowFromTs = null, // smoke: not tied to a channel window, so confirm won't move a cursor
            WindowToTs = null,
            CreatedUtc = _clock.GetUtcNow(),
        }).ToList();

        _db.SummaryCandidates.AddRange(candidates);
        await _db.SaveChangesAsync(ct);
        return new("Extracted", candidates);
    }

    public async Task<string> ConfirmAsync(Guid candidateId, string? confirmingUser, CancellationToken ct = default)
    {
        var candidate = await _db.SummaryCandidates.FirstOrDefaultAsync(c => c.Id == candidateId, ct);
        if (candidate is null) return "NotFound";

        await _writeBack.SubmitAsync(new WriteBackSubmission
        {
            WorkItemKey = candidate.WorkItemKey,
            RecordIdentity = candidate.RecordIdentity,
            Kind = candidate.Kind,
            Content = _redactor.Redact(candidate.Content),
            SourceUrl = $"slack://{candidate.ChannelId}",
            Author = confirmingUser,
        }, ct);

        candidate.Status = CandidateStatus.Confirmed;

        // Advance the channel cursor to this window's end — only on a successful confirm/write-back.
        if (candidate.WindowToTs is not null)
            await AdvanceCursorAsync(candidate.ChannelId, candidate.WorkItemKey, candidate.WindowToTs, ct);

        await _db.SaveChangesAsync(ct);
        return "Confirmed";
    }

    public async Task<string> RejectAsync(Guid candidateId, CancellationToken ct = default)
    {
        var candidate = await _db.SummaryCandidates.FirstOrDefaultAsync(c => c.Id == candidateId, ct);
        if (candidate is null) return "NotFound";
        candidate.Status = CandidateStatus.Rejected; // cursor unchanged — the window can be retried
        await _db.SaveChangesAsync(ct);
        return "Rejected";
    }

    private async Task AdvanceCursorAsync(string channelId, string workItemKey, string toTs, CancellationToken ct)
    {
        var cursor = await _db.PostCursors.FirstOrDefaultAsync(c => c.ChannelId == channelId, ct);
        if (cursor is null)
        {
            cursor = new PostCursor { ChannelId = channelId, WorkItemKey = workItemKey };
            _db.PostCursors.Add(cursor);
        }
        // Never move the cursor backwards.
        if (cursor.LastPostedTs is null || string.Compare(toTs, cursor.LastPostedTs) > 0)
        {
            cursor.LastPostedTs = toTs;
            cursor.UpdatedUtc = _clock.GetUtcNow();
        }
    }

    private static DateTimeOffset TsToUtc(string ts)
        => double.TryParse(ts.Split('.')[0], out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds((long)seconds)
            : DateTimeOffset.MinValue;

    private static string StableHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
