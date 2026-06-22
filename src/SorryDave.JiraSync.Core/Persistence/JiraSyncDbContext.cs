using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SorryDave.JiraSync.Core.Domain;

namespace SorryDave.JiraSync.Core.Persistence;

public class JiraSyncDbContext : DbContext
{
    public JiraSyncDbContext(DbContextOptions<JiraSyncDbContext> options) : base(options) { }

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<ResourceMapping> ResourceMappings => Set<ResourceMapping>();
    public DbSet<WriteBackRecord> WriteBackRecords => Set<WriteBackRecord>();
    public DbSet<CapturedMessage> CapturedMessages => Set<CapturedMessage>();
    public DbSet<SummaryCandidate> SummaryCandidates => Set<SummaryCandidate>();
    public DbSet<PostCursor> PostCursors => Set<PostCursor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var labelsComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => (v ?? new()).Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            v => v.ToList());

        modelBuilder.Entity<WorkItem>(b =>
        {
            b.HasKey(w => w.Key);
            b.Property(w => w.Key).HasMaxLength(64);
            b.Property(w => w.ProjectKey).HasMaxLength(64);
            b.Property(w => w.Summary).HasMaxLength(1024);
            b.Property(w => w.Labels)
                .HasConversion(
                    v => string.Join('\n', v),
                    v => v.Length == 0 ? new List<string>() : v.Split('\n', StringSplitOptions.None).ToList())
                .Metadata.SetValueComparer(labelsComparer);
            b.Property(w => w.MentionedAccountIds)
                .HasConversion(
                    v => string.Join('\n', v),
                    v => v.Length == 0 ? new List<string>() : v.Split('\n', StringSplitOptions.None).ToList())
                .Metadata.SetValueComparer(labelsComparer);
            b.HasIndex(w => w.ProjectKey);
            b.HasIndex(w => w.JiraUpdated);
        });

        modelBuilder.Entity<ResourceMapping>(b =>
        {
            b.HasKey(m => m.Id);
            b.Property(m => m.ResourceId).HasMaxLength(256);
            // At most one mapping per (type, resource id) — enforces "unique per resource".
            b.HasIndex(m => new { m.ResourceType, m.ResourceId }).IsUnique();
            b.HasOne(m => m.WorkItem)
                .WithMany(w => w.Mappings)
                .HasForeignKey(m => m.WorkItemKey)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WriteBackRecord>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.RecordIdentity).HasMaxLength(256);
            // Identity of a logical record — resubmission edits in place.
            b.HasIndex(r => new { r.WorkItemKey, r.RecordIdentity }).IsUnique();
            b.HasIndex(r => new { r.Status, r.NextAttemptUtc });
        });

        modelBuilder.Entity<CapturedMessage>(b =>
        {
            b.HasKey(m => m.Id);
            b.Property(m => m.ChannelId).HasMaxLength(64);
            b.Property(m => m.Ts).HasMaxLength(32);
            // One row per (channel, ts) — redelivery is an upsert; window queries order by ts.
            b.HasIndex(m => new { m.ChannelId, m.Ts }).IsUnique();
        });

        modelBuilder.Entity<SummaryCandidate>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.RecordIdentity).HasMaxLength(256);
            b.HasIndex(c => new { c.WorkItemKey, c.Status });
        });

        modelBuilder.Entity<PostCursor>(b =>
        {
            b.HasKey(c => c.ChannelId);
            b.Property(c => c.ChannelId).HasMaxLength(64);
        });
    }
}
