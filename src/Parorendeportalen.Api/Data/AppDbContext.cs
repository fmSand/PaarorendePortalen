using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Integrations.Sync;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Visit> Visits => Set<Visit>();

    public DbSet<CareRecipient> CareRecipients => Set<CareRecipient>();

    public DbSet<NextOfKin> NextOfKin => Set<NextOfKin>();

    public DbSet<KinshipGrant> KinshipGrants => Set<KinshipGrant>();

    public DbSet<Consent> Consents => Set<Consent>();

    public DbSet<AccessLogEntry> AccessLogEntries => Set<AccessLogEntry>();

    public DbSet<SyncWatermark> SyncWatermarks => Set<SyncWatermark>();

    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Visit>(visit =>
        {
            visit.Property(v => v.Status).HasConversion<string>();

            visit
                .HasOne(v => v.CareRecipient)
                .WithMany(c => c.Visits)
                .HasForeignKey(v => v.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            visit.Property(v => v.Origin).HasConversion<string>().HasMaxLength(50);
            visit.Property(v => v.ExternalId).HasMaxLength(256);

            // ExternalId leads so ingestion can seek on it; a leading Origin
            // filtered with <> cannot bound the scan. Filtered on NOT NULL so
            // the rule is in the schema r
            visit
                .HasIndex(v => new { v.ExternalId, v.Origin })
                .IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");
        });

        modelBuilder.Entity<CareRecipient>(careRecipient =>
        {
            careRecipient.Property(c => c.NationalIdHash).HasMaxLength(64).IsFixedLength();

            // Filtered for the same reason as the Visits index: a recipient
            // the portal has no number for is a row sync is meant to skip.
            careRecipient
                .HasIndex(c => c.NationalIdHash)
                .IsUnique()
                .HasFilter("\"NationalIdHash\" IS NOT NULL");
        });

        modelBuilder.Entity<SyncWatermark>(watermark =>
        {
            watermark.Property(w => w.SourceSystem).HasConversion<string>().HasMaxLength(50);
            watermark.Property(w => w.ResourceType).HasConversion<string>().HasMaxLength(50);
            watermark.Property(w => w.ContinuationToken).HasMaxLength(512);

            watermark.HasIndex(w => new { w.SourceSystem, w.ResourceType }).IsUnique();
        });

        modelBuilder.Entity<SyncRun>(run =>
        {
            run.Property(r => r.SourceSystem).HasConversion<string>().HasMaxLength(50);
            run.Property(r => r.ResourceType).HasConversion<string>().HasMaxLength(50);
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(50);
            run.Property(r => r.Error).HasMaxLength(2000);

            run.HasIndex(r => new
            {
                r.SourceSystem,
                r.ResourceType,
                r.StartedAt,
            });
        });

        modelBuilder.Entity<NextOfKin>(nextOfKin =>
        {
            nextOfKin.HasIndex(n => n.ExternalId).IsUnique();

            nextOfKin.HasIndex(n => n.NationalIdHash).IsUnique();

            nextOfKin.Property(n => n.ExternalId).HasMaxLength(256);
            nextOfKin.Property(n => n.NationalIdHash).HasMaxLength(64).IsFixedLength();
            nextOfKin.Property(n => n.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<KinshipGrant>(grant =>
        {
            grant
                .HasOne(g => g.NextOfKin)
                .WithMany(n => n.Grants)
                .HasForeignKey(g => g.NextOfKinId)
                .OnDelete(DeleteBehavior.Cascade);

            grant
                .HasOne(g => g.CareRecipient)
                .WithMany(c => c.Grants)
                .HasForeignKey(g => g.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique per pair: a revoked grant is closed with ValidTo (not deleted)
            grant.HasIndex(g => new { g.NextOfKinId, g.CareRecipientId }).IsUnique();

            grant.Property(g => g.Relationship).HasMaxLength(100);
        });

        modelBuilder.Entity<Consent>(consent =>
        {
            consent.Property(c => c.Category).HasConversion<string>().HasMaxLength(50);

            consent
                .HasOne(c => c.NextOfKin)
                .WithMany(n => n.Consents)
                .HasForeignKey(c => c.NextOfKinId)
                .OnDelete(DeleteBehavior.Cascade);

            consent
                .HasOne(c => c.CareRecipient)
                .WithMany(r => r.Consents)
                .HasForeignKey(c => c.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            // One open consent per triple. A revoked row has a ValidTo and drops
            // out of the filter, so history can hold any number of closed rows.
            consent
                .HasIndex(c => new
                {
                    c.CareRecipientId,
                    c.NextOfKinId,
                    c.Category,
                })
                .IsUnique()
                .HasFilter("\"ValidTo\" IS NULL");
        });

        modelBuilder.Entity<AccessLogEntry>(entry =>
        {
            entry.Property(e => e.Category).HasConversion<string>().HasMaxLength(50);
            entry.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(50);

            entry.HasIndex(e => new { e.CareRecipientId, e.OccurredAt });
            entry.HasIndex(e => new { e.NextOfKinId, e.OccurredAt });
        });
    }
}
