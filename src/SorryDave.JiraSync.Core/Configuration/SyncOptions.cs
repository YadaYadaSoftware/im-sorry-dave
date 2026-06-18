namespace SorryDave.JiraSync.Core.Configuration;

public class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>How often the reconciliation sweep runs.</summary>
    public TimeSpan ReconciliationInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Overlap subtracted from the last sweep time when building the JQL window, to tolerate
    /// clock skew and ensure no updates fall between sweeps.
    /// </summary>
    public TimeSpan ReconciliationOverlap { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Run a full backfill over the tracked projects on startup.</summary>
    public bool BackfillOnStartup { get; set; } = true;

    /// <summary>How often the write-back outbox sender drains pending records.</summary>
    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Maximum delivery attempts before a record is marked permanently failed.</summary>
    public int MaxWriteBackAttempts { get; set; } = 8;
}
