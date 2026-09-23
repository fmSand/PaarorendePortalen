using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Parorendeportalen.Api.Integrations.Sync;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Models.Kinship;
using Parorendeportalen.Api.Models.Notifications;
using Parorendeportalen.Api.Models.Planning;
using Parorendeportalen.Api.Models.Visits;

namespace Parorendeportalen.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Visit> Visits => Set<Visit>();

    public DbSet<VisitComment> VisitComments => Set<VisitComment>();

    public DbSet<CareRecipient> CareRecipients => Set<CareRecipient>();

    public DbSet<NextOfKin> NextOfKin => Set<NextOfKin>();

    public DbSet<KinshipGrant> KinshipGrants => Set<KinshipGrant>();

    public DbSet<Vedtak> Vedtak => Set<Vedtak>();

    public DbSet<VedtakTask> VedtakTasks => Set<VedtakTask>();

    public DbSet<Consent> Consents => Set<Consent>();

    public DbSet<AccessLogEntry> AccessLogEntries => Set<AccessLogEntry>();

    public DbSet<ChangeEvent> ChangeEvents => Set<ChangeEvent>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    public DbSet<SyncWatermark> SyncWatermarks => Set<SyncWatermark>();

    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Visit>(visit =>
        {
            visit.Property(v => v.Status).HasConversion<string>();

            // Fixed at insert: it scopes who may read the visit and the comments under it.
            visit
                .Property(v => v.CareRecipientId)
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

            visit
                .HasOne(v => v.CareRecipient)
                .WithMany(c => c.Visits)
                .HasForeignKey(v => v.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            visit.Property(v => v.Origin).HasConversion<string>().HasMaxLength(50);
            visit.Property(v => v.ServiceType).HasConversion<string>().HasMaxLength(50);
            visit.Property(v => v.ExternalId).HasMaxLength(256);
            visit.Property(v => v.Title).HasMaxLength(200);
            visit.Property(v => v.Visibility).HasConversion<string>().HasMaxLength(50);

            // Maps to xmin, the system column Postgres keeps on every row.
            visit.Property(v => v.Version).IsRowVersion();

            // Restrict: deleting a person must not delete a calendar entry the family shares.
            visit
                .HasOne(v => v.CreatedBy)
                .WithMany()
                .HasForeignKey(v => v.CreatedByNextOfKinId)
                .OnDelete(DeleteBehavior.Restrict);

            // Author and visibility are both null or both set; read filter treats author-less rows as everyone's.
            visit.ToTable(table =>
                table.HasCheckConstraint(
                    "CK_Visits_AuthoredEntryHasVisibility",
                    "(\"CreatedByNextOfKinId\" IS NULL) = (\"Visibility\" IS NULL)"
                )
            );

            // ExternalId leads so ingestion can seek on it; a leading Origin filtered with <>
            // cannot bound the scan. Filtered on NOT NULL, since a portal row has no ExternalId.
            visit
                .HasIndex(v => new { v.ExternalId, v.Origin })
                .IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");
        });

        modelBuilder.Entity<VisitComment>(comment =>
        {
            comment.Property(c => c.Body).HasMaxLength(2000);
            comment.Property(c => c.Visibility).HasConversion<string>().HasMaxLength(50);
            comment.Property(c => c.Version).IsRowVersion();

            comment
                .HasOne(c => c.Visit)
                .WithMany(v => v.Comments)
                .HasForeignKey(c => c.VisitId)
                .OnDelete(DeleteBehavior.Cascade);

            comment
                .HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorNextOfKinId)
                .OnDelete(DeleteBehavior.Restrict);

            // The thread under one visit, oldest first.
            comment.HasIndex(c => new { c.VisitId, c.CreatedAt });
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

            // Unique per pair. A revoked grant is closed with ValidTo and keeps its row.
            grant.HasIndex(g => new { g.NextOfKinId, g.CareRecipientId }).IsUnique();

            grant.Property(g => g.Relationship).HasMaxLength(100);
        });

        modelBuilder.Entity<Vedtak>(vedtak =>
        {
            vedtak.Property(v => v.ServiceType).HasConversion<string>().HasMaxLength(50);
            vedtak.Property(v => v.Status).HasConversion<string>().HasMaxLength(50);
            vedtak.Property(v => v.Title).HasMaxLength(300);

            vedtak
                .HasOne(v => v.CareRecipient)
                .WithMany()
                .HasForeignKey(v => v.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            vedtak.OwnsOne(
                v => v.Recurrence,
                recurrence =>
                {
                    // The one enum not stored as a string: as text, a flag set becomes a
                    // comma-separated list no query could test a single day against.
                    recurrence.Property(r => r.Days).HasColumnName("RecurrenceDays");
                    recurrence.Property(r => r.TimesPerDay).HasColumnName("RecurrenceTimesPerDay");
                }
            );

            // Both reads start here: the vedtak page and the day plan's in-force filter.
            vedtak.HasIndex(v => new { v.CareRecipientId, v.ValidFrom });
        });

        modelBuilder.Entity<VedtakTask>(task =>
        {
            task.Property(t => t.Description).HasMaxLength(500);

            task.HasOne(t => t.Vedtak)
                .WithMany(v => v.Tasks)
                .HasForeignKey(t => t.VedtakId)
                .OnDelete(DeleteBehavior.Cascade);

            task.HasIndex(t => new { t.VedtakId, t.Sequence });
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
            entry.Property(e => e.Operation).HasConversion<string>().HasMaxLength(50);
            entry.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(50);

            entry.HasIndex(e => new { e.CareRecipientId, e.OccurredAt });
            entry.HasIndex(e => new { e.NextOfKinId, e.OccurredAt });
        });

        modelBuilder.Entity<ChangeEvent>(change =>
        {
            change.Property(c => c.Category).HasConversion<string>().HasMaxLength(50);
            change.Property(c => c.Kind).HasConversion<string>().HasMaxLength(50);

            change
                .HasOne(c => c.CareRecipient)
                .WithMany()
                .HasForeignKey(c => c.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            change
                .HasOne(c => c.Visit)
                .WithMany()
                .HasForeignKey(c => c.VisitId)
                .OnDelete(DeleteBehavior.Cascade);

            // The fan-out reads the unprocessed tail, oldest first.
            change.HasIndex(c => c.ProcessedAt);
        });

        modelBuilder.Entity<Notification>(notification =>
        {
            notification.Property(n => n.Category).HasConversion<string>().HasMaxLength(50);
            notification.Property(n => n.Kind).HasConversion<string>().HasMaxLength(50);

            notification
                .HasOne(n => n.NextOfKin)
                .WithMany()
                .HasForeignKey(n => n.NextOfKinId)
                .OnDelete(DeleteBehavior.Cascade);

            notification
                .HasOne(n => n.CareRecipient)
                .WithMany()
                .HasForeignKey(n => n.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            // One copy per person per change, so a tick repeated after a crash cannot deliver twice.
            notification.HasIndex(n => new { n.ChangeEventId, n.NextOfKinId }).IsUnique();

            notification.HasIndex(n => new { n.NextOfKinId, n.OccurredAt });
        });

        modelBuilder.Entity<NotificationPreference>(preference =>
        {
            preference.Property(p => p.Kind).HasConversion<string>().HasMaxLength(50);

            preference
                .HasOne(p => p.NextOfKin)
                .WithMany()
                .HasForeignKey(p => p.NextOfKinId)
                .OnDelete(DeleteBehavior.Cascade);

            preference.HasIndex(p => new { p.NextOfKinId, p.Kind }).IsUnique();
        });
    }
}
