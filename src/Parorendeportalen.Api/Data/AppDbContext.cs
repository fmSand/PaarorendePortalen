using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Visit> Visits => Set<Visit>();

    public DbSet<CareRecipient> CareRecipients => Set<CareRecipient>();

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
    }
}
