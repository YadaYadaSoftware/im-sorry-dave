using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.WriteBack;

/// <summary>
/// Formats a managed write-back into a Jira comment body and embeds a machine-readable
/// marker so the record can be recognised (and recovered) from Jira itself. The marker is a
/// secondary idempotency key alongside the outbox row.
/// </summary>
public static class RecordMarker
{
    public const string Prefix = "managed-record";

    public static string BuildMarker(WriteBackKind kind, string recordIdentity)
        => $"[{Prefix}:{kind}:{recordIdentity}]";

    public static string BuildCommentBody(WriteBackRecord record)
    {
        var header = record.Kind switch
        {
            WriteBackKind.Decision => "Decision",
            WriteBackKind.Answer => "Answer",
            WriteBackKind.Summary => "Summary",
            _ => "Note"
        };

        var attribution = (record.SourceUrl, record.Author) switch
        {
            (not null, not null) => $"Source: {record.SourceUrl} · by {record.Author}",
            (not null, null) => $"Source: {record.SourceUrl}",
            (null, not null) => $"Recorded by {record.Author}",
            _ => "Recorded automatically"
        };

        // Blank-line separated paragraphs map cleanly onto ADF paragraphs.
        return $"*{header}*\n\n{record.Content}\n\n{attribution}\n\n{BuildMarker(record.Kind, record.RecordIdentity)}";
    }
}
