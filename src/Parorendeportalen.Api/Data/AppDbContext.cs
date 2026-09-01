using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Visit> Visits => Set<Visit>();

    public DbSet<CareRecipient> CareRecipients => Set<CareRecipient>();

    public DbSet<NextOfKin> NextOfKin => Set<NextOfKin>();

    public DbSet<KinshipGrant> KinshipGrants => Set<KinshipGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Visit>(visit =>
        {
            visit.Property(v => v.Status).HasConversion<string>();

            visit.HasOne(v => v.CareRecipient)
                .WithMany(c => c.Visits)
                .HasForeignKey(v => v.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            visit.Property(v => v.Origin).HasConversion<string>().HasMaxLength(50);
            visit.Property(v => v.ExternalId).HasMaxLength(256);

            // Filtered so the rule is stated in the schema rather than left to
            // Postgres treating NULLs as distinct.
            visit.HasIndex(v => new { v.Origin, v.ExternalId })
                .IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");
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
            grant.HasOne(g => g.NextOfKin)
                .WithMany(n => n.Grants)
                .HasForeignKey(g => g.NextOfKinId)
                .OnDelete(DeleteBehavior.Cascade);

            grant.HasOne(g => g.CareRecipient)
                .WithMany(c => c.Grants)
                .HasForeignKey(g => g.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique per pair: a revoked grant is closed with ValidTo (not deleted)
            grant.HasIndex(g => new { g.NextOfKinId, g.CareRecipientId }).IsUnique();

            grant.Property(g => g.Relationship).HasMaxLength(100);
        });
    }
}
