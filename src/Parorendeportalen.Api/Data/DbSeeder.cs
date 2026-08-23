using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Data;

public static class DbSeeder
{
    public static void SeedIfEmpty(
        AppDbContext context, NationalIdHasher hasher, IConfiguration configuration, IHostEnvironment environment)
    {
        if (context.CareRecipients.Any())
        {
            return;
        }

        var kari = new CareRecipient { Name = "Kari Nordmann" };

        context.CareRecipients.Add(kari);

        context.Visits.AddRange(
            new Visit
            {
                CareRecipient = kari,
                ScheduledAt = DateTimeOffset.UtcNow.AddHours(-3),
                ActualAt = DateTimeOffset.UtcNow.AddHours(-3).AddMinutes(5),
                Status = VisitStatus.Completed,
                CaregiverName = "Hjemmetjenesten Oslo",
                Notes = "Morgenstell og medisiner gitt."
            },
            new Visit
            {
                CareRecipient = kari,
                ScheduledAt = DateTimeOffset.UtcNow.AddHours(2),
                Status = VisitStatus.Planned,
                CaregiverName = "Hjemmetjenesten Oslo"
            },
            new Visit
            {
                CareRecipient = kari,
                ScheduledAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-6),
                Status = VisitStatus.Missed,
                CaregiverName = "Hjemmetjenesten Oslo",
                Notes = "Ingen oppmøte registrert."
            });

        foreach (var grant in configuration.GetSection("Kinship:SeedGrants").GetChildren())
        {
            var nationalId = grant["NationalId"];
            if (string.IsNullOrWhiteSpace(nationalId))
            {
                continue;
            }

            context.NextOfKin.Add(new NextOfKin
            {
                CareRecipient = kari,
                NationalIdHash = hasher.Hash(nationalId),
                DisplayName = grant["DisplayName"] ?? "Pårørende",
                Relationship = grant["Relationship"]
            });
        }

        context.SaveChanges();
    }
}
