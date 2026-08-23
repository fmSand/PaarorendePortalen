using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Visit> Visits => Set<Visit>();

    public DbSet<CareRecipient> CareRecipients => Set<CareRecipient>();

    public DbSet<NextOfKin> NextOfKin => Set<NextOfKin>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Visit>(visit =>
        {
            visit.Property(v => v.Status).HasConversion<string>();

            visit.HasOne(v => v.CareRecipient)
                .WithMany(c => c.Visits)
                .HasForeignKey(v => v.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NextOfKin>(nextOfKin =>
        {
            nextOfKin.HasOne(n => n.CareRecipient)
                .WithMany(c => c.NextOfKin)
                .HasForeignKey(n => n.CareRecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            nextOfKin.HasIndex(n => n.ExternalId).IsUnique();

            nextOfKin.HasIndex(n => n.NationalIdHash).IsUnique();

            nextOfKin.Property(n => n.ExternalId).HasMaxLength(256);
            nextOfKin.Property(n => n.NationalIdHash).HasMaxLength(64).IsFixedLength();
            nextOfKin.Property(n => n.DisplayName).HasMaxLength(200);
            nextOfKin.Property(n => n.Relationship).HasMaxLength(100);
        });
    }
}
