using Parorendeportalen.Api.Authentication;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Data;

public static class DbSeeder
{
    public static void SeedIfEmpty(
        AppDbContext context,
        NationalIdHasher hasher,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        if (context.CareRecipients.Any())
        {
            return;
        }

        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        var tor = new CareRecipient { Name = "Tor Quist" };

        context.CareRecipients.AddRange(vigdis, tor);

        // Stable ExternalIds let a re-run of sync be a no-op.
        context.Visits.AddRange(
            new Visit
            {
                CareRecipient = vigdis,
                ScheduledAt = DateTimeOffset.UtcNow.AddHours(-3),
                ActualAt = DateTimeOffset.UtcNow.AddHours(-3).AddMinutes(5),
                Status = VisitStatus.Completed,
                CaregiverName = "Hjemmetjenesten Oslo",
                Notes = "Morgenstell og medisiner gitt.",
                Origin = Origin.Synthetic,
                ExternalId = "synthetic-vigdis-0001",
            },
            new Visit
            {
                CareRecipient = vigdis,
                ScheduledAt = DateTimeOffset.UtcNow.AddHours(2),
                Status = VisitStatus.Planned,
                CaregiverName = "Hjemmetjenesten Oslo",
                Origin = Origin.Synthetic,
                ExternalId = "synthetic-vigdis-0002",
            },
            new Visit
            {
                CareRecipient = vigdis,
                ScheduledAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-6),
                Status = VisitStatus.Missed,
                CaregiverName = "Hjemmetjenesten Oslo",
                Notes = "Ingen oppmøte registrert.",
                Origin = Origin.Synthetic,
                ExternalId = "synthetic-vigdis-0003",
            },
            new Visit
            {
                CareRecipient = tor,
                ScheduledAt = DateTimeOffset.UtcNow.AddHours(-1),
                ActualAt = DateTimeOffset.UtcNow.AddHours(-1).AddMinutes(12),
                Status = VisitStatus.Completed,
                CaregiverName = "Hjemmetjenesten Oslo",
                Notes = "Tilsyn og måltidsstøtte.",
                Origin = Origin.Synthetic,
                ExternalId = "synthetic-tor-0001",
            },
            new Visit
            {
                CareRecipient = tor,
                ScheduledAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(3),
                Status = VisitStatus.Planned,
                CaregiverName = "Hjemmetjenesten Oslo",
                Origin = Origin.Synthetic,
                ExternalId = "synthetic-tor-0002",
            }
        );

        // National ids stay in user-secrets. Use synthetic numbers from
        // Skatteetaten's Tenor (Test-Norge)
        foreach (var seedGrant in configuration.GetSection("Kinship:SeedGrants").GetChildren())
        {
            var nationalId = seedGrant["NationalId"];
            if (string.IsNullOrWhiteSpace(nationalId))
            {
                continue;
            }

            AddPersonWithGrantsTo(
                context,
                externalId: null,
                nationalIdHash: hasher.Hash(nationalId),
                displayName: seedGrant["DisplayName"] ?? "Pårørende",
                relationship: seedGrant["Relationship"],
                vigdis,
                tor
            );
        }

        if (environment.EnvironmentName == "Demo")
        {
            AddPersonWithGrantsTo(
                context,
                externalId: DemoAuthenticationHandler.ExternalId,
                nationalIdHash: hasher.Hash($"demo-{DemoAuthenticationHandler.ExternalId}"),
                displayName: "Demo Pårørende",
                relationship: "Demo",
                vigdis,
                tor
            );
        }

        context.SaveChanges();
    }

    private static void AddPersonWithGrantsTo(
        AppDbContext context,
        string? externalId,
        string nationalIdHash,
        string displayName,
        string? relationship,
        params CareRecipient[] careRecipients
    )
    {
        var person = new NextOfKin
        {
            ExternalId = externalId,
            NationalIdHash = nationalIdHash,
            DisplayName = displayName,
        };

        person.Grants.AddRange(
            careRecipients.Select(careRecipient => new KinshipGrant
            {
                CareRecipient = careRecipient,
                Relationship = relationship,
            })
        );

        context.NextOfKin.Add(person);
    }
}
