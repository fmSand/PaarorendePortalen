using Parorendeportalen.Api.Authentication;
using Parorendeportalen.Api.Extensions;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Models.Kinship;
using Parorendeportalen.Api.Models.Notifications;
using Parorendeportalen.Api.Models.Planning;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Services.Kinship;

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

    // Same problem as the identity backfill: SeedIfEmpty returns early on a database
    // that already has care recipients, so one seeded before vedtak existed needs this.
    public static void BackfillVedtak(AppDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.CareRecipients.Any() || context.Vedtak.Any())
        {
            return;
        }

        var careRecipients = context.CareRecipients.OrderBy(c => c.Id).ToList();

        foreach (var careRecipient in careRecipients)
        {
            context.Vedtak.AddRange(StandInVedtakFor(careRecipient));
        }

        // Only where the stand-in consent component already granted the visit
        // log. A person who never consented to anything is not given one here.
        var consented = context
            .Consents.Where(c => c.Category == DataCategory.Visits && c.ValidTo == null)
            .ToList();

        // Vedtak rows can be dropped without the consents that came with them,
        // and the open-consent index answers the duplicate with 23505 at boot.
        var existing = context
            .Consents.Where(c => c.Category == DataCategory.Vedtak && c.ValidTo == null)
            .Select(c => new { c.NextOfKinId, c.CareRecipientId })
            .ToHashSet();

        foreach (
            var grant in consented.Where(c =>
                !existing.Contains(new { c.NextOfKinId, c.CareRecipientId })
            )
        )
        {
            context.Consents.Add(
                new Consent
                {
                    NextOfKinId = grant.NextOfKinId,
                    CareRecipientId = grant.CareRecipientId,
                    Category = DataCategory.Vedtak,
                }
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

        // No source serves vedtak yet, so every recipient gets theirs seeded,
        // including the ones whose visits arrive through sync.
        foreach (var careRecipient in careRecipients)
        {
            context.Vedtak.AddRange(StandInVedtakFor(careRecipient));
        }

        // Hand-seeded synthetic rows are orphans no source can reconcile, so
        // they only stand in where sync has no number to find the recipient by.
        // Their change events come with them, so the inbox has something.
        for (var index = 0; index < careRecipients.Count; index++)
        {
            if (careRecipients[index].NationalIdHash is null)
            {
                var visits = StandInVisitsFor(careRecipients[index], index);
                context.Visits.AddRange(visits);
                context.ChangeEvents.AddRange(visits.Select(StandInEventFor));
            }
        }

        // National ids stay in user-secrets. Use synthetic numbers from Skatteetaten's Tenor (Test-Norge)
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

        if (environment.IsDemo())
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

    // The seed list decides who exists. A name typed into configuration creates
    // that person, so the synthetic feed always points at someone the portal
    // holds. Without a seed list the demo still has people to show.
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

    private static ChangeEvent StandInEventFor(Visit visit) =>
        new()
        {
            CareRecipient = visit.CareRecipient,
            Category = DataCategory.Visits,
            Kind = visit.Status switch
            {
                VisitStatus.Completed => ChangeKind.Completed,
                VisitStatus.Missed => ChangeKind.Missed,
                VisitStatus.Cancelled => ChangeKind.Cancelled,
                _ => ChangeKind.Added,
            },
            Visit = visit,
            ScheduledAt = visit.ScheduledAt,
            OccurredAt = DateTimeOffset.UtcNow,
        };

    // Hjemmesykepleie matches the synthetic feed's two daily slots, so its occurrences
    // get settled. Fysioterapi has no visits on purpose, to show one nothing was reported for.
    private static List<Vedtak> StandInVedtakFor(CareRecipient careRecipient)
    {
        var today = NorwegianTime.DateOf(DateTimeOffset.UtcNow);

        var hjemmesykepleie = new Vedtak
        {
            CareRecipient = careRecipient,
            ServiceType = ServiceType.Hjemmesykepleie,
            Title = "Hjemmesykepleie x2/dag",
            Recurrence = new RecurrenceRule { Days = Weekdays.EveryDay, TimesPerDay = 2 },
            ValidFrom = today.AddDays(-90),
            Status = VedtakStatus.Active,
        };

        hjemmesykepleie.Tasks.AddRange([
            new VedtakTask { Description = "Morgenstell og påkledning", Sequence = 1 },
            new VedtakTask { Description = "Utdeling av medisiner", Sequence = 2 },
            new VedtakTask { Description = "Tilsyn og måltidsstøtte", Sequence = 3 },
            new VedtakTask { Description = "Kveldsstell", Sequence = 4 },
        ]);

        var fysioterapi = new Vedtak
        {
            CareRecipient = careRecipient,
            ServiceType = ServiceType.Fysioterapi,
            Title = "Fysioterapi hver onsdag",
            Recurrence = new RecurrenceRule { Days = Weekdays.Wednesday, TimesPerDay = 1 },
            ValidFrom = today.AddDays(-30),
            ValidTo = today.AddDays(150),
            Status = VedtakStatus.Active,
        };

        fysioterapi.Tasks.Add(
            new VedtakTask { Description = "Gangtrening og balanseøvelser", Sequence = 1 }
        );

        return [hjemmesykepleie, fysioterapi];
    }

    private static List<Visit> StandInVisitsFor(CareRecipient careRecipient, int index) =>
        [
            new Visit
            {
                CareRecipient = careRecipient,
                ScheduledAt = DateTimeOffset.UtcNow.AddHours(-3),
                ActualAt = DateTimeOffset.UtcNow.AddHours(-3).AddMinutes(5),
                Status = VisitStatus.Completed,
                ServiceType = ServiceType.Hjemmesykepleie,
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
                ServiceType = ServiceType.Hjemmesykepleie,
                CaregiverName = "Hjemmetjenesten Oslo",
                Origin = Origin.Synthetic,
                ExternalId = $"seeded-{index:D2}-0002",
            },
            new Visit
            {
                CareRecipient = careRecipient,
                ScheduledAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-6),
                Status = VisitStatus.Missed,
                ServiceType = ServiceType.Hjemmesykepleie,
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

        // Stands in for the national consent component. The day plan reads both
        // categories, so a database seeded with only Visits would 403 it.
        person.Consents.AddRange(
            careRecipients.SelectMany(careRecipient =>
                new[] { DataCategory.Visits, DataCategory.Vedtak }.Select(category => new Consent
                {
                    CareRecipient = careRecipient,
                    Category = category,
                })
            )
        );

        context.NextOfKin.Add(person);
    }
}
