using Parorendeportalen.Api.Authentication;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Data;

public static class DbSeeder
{
    // A database seeded before the column existed never gets a hash out of
    // SeedIfEmpty, which returns early on a table that already has rows. Sync
    // would then resolve nothing against it, for good.
    public static void BackfillCareRecipientIdentities(
        AppDbContext context,
        NationalIdHasher hasher,
        IConfiguration configuration,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        var identities = CareRecipientSeedReader.Read(configuration);
        if (identities.Count == 0 || !context.CareRecipients.Any())
        {
            return;
        }

        var names = identities.Select(seed => seed.Name).ToList();
        var rows = context.CareRecipients.Where(c => names.Contains(c.Name)).ToList();

        foreach (var row in rows.Where(row => row.NationalIdHash is null))
        {
            var identity = identities.First(seed => seed.Name == row.Name);
            row.NationalIdHash = hasher.Hash(identity.NationalIdentifier.HashInput);
        }

        // SeedIfEmpty has already returned on a table with rows, so a name that
        // drifted from the seed list leaves that person unreachable to sync,
        // with an unresolved count as the only other sign of it.
        foreach (var missed in identities.Where(seed => !rows.Exists(row => row.Name == seed.Name)))
        {
            logger.LogWarning(
                "The care recipient seed names '{Name}' and no row in this database carries that name. Visits for that person will not resolve.",
                missed.Name
            );
        }

        context.SaveChanges();
    }

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

        var careRecipients = SeededCareRecipients(configuration, hasher);
        context.CareRecipients.AddRange(careRecipients);

        // Hand-seeded synthetic rows are orphans no source can reconcile, so
        // they only stand in where sync has no number to find the recipient by.
        for (var index = 0; index < careRecipients.Count; index++)
        {
            if (careRecipients[index].NationalIdHash is null)
            {
                context.Visits.AddRange(StandInVisitsFor(careRecipients[index], index));
            }
        }

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
                careRecipients
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
                careRecipients
            );
        }

        context.SaveChanges();
    }

    // The seed list decides who exists, so a name typed into configuration
    // creates that person rather than leaving the synthetic feed pointing at
    // someone the portal does not hold. Without a seed list the demo still has
    // people to show.
    private static List<CareRecipient> SeededCareRecipients(
        IConfiguration configuration,
        NationalIdHasher hasher
    )
    {
        var identities = CareRecipientSeedReader.Read(configuration);

        if (identities.Count == 0)
        {
            return
            [
                new CareRecipient { Name = "Vigdis Quist" },
                new CareRecipient { Name = "Tor Quist" },
            ];
        }

        return
        [
            .. identities.Select(seed => new CareRecipient
            {
                Name = seed.Name,
                NationalIdHash = hasher.Hash(seed.NationalIdentifier.HashInput),
            }),
        ];
    }

    private static IEnumerable<Visit> StandInVisitsFor(CareRecipient careRecipient, int index) =>
        [
            new Visit
            {
                CareRecipient = careRecipient,
                ScheduledAt = DateTimeOffset.UtcNow.AddHours(-3),
                ActualAt = DateTimeOffset.UtcNow.AddHours(-3).AddMinutes(5),
                Status = VisitStatus.Completed,
                CaregiverName = "Hjemmetjenesten Oslo",
                Notes = "Morgenstell og medisiner gitt.",
                Origin = Origin.Synthetic,
                ExternalId = $"seeded-{index:D2}-0001",
            },
            new Visit
            {
                CareRecipient = careRecipient,
                ScheduledAt = DateTimeOffset.UtcNow.AddHours(2),
                Status = VisitStatus.Planned,
                CaregiverName = "Hjemmetjenesten Oslo",
                Origin = Origin.Synthetic,
                ExternalId = $"seeded-{index:D2}-0002",
            },
            new Visit
            {
                CareRecipient = careRecipient,
                ScheduledAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-6),
                Status = VisitStatus.Missed,
                CaregiverName = "Hjemmetjenesten Oslo",
                Notes = "Ingen oppmøte registrert.",
                Origin = Origin.Synthetic,
                ExternalId = $"seeded-{index:D2}-0003",
            },
        ];

    private static void AddPersonWithGrantsTo(
        AppDbContext context,
        string? externalId,
        string nationalIdHash,
        string displayName,
        string? relationship,
        IReadOnlyList<CareRecipient> careRecipients
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
