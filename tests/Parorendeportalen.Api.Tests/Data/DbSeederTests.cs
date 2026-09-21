using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Models.Kinship;
using Parorendeportalen.Api.Models.Notifications;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Notifications;
using Parorendeportalen.Api.Repositories.Access;
using Parorendeportalen.Api.Repositories.Notifications;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Services.Kinship;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Data;

[Collection(PostgresCollection.Name)]
public class DbSeederTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = Snapshots.Noon;

    private readonly NationalIdHasher _hasher = new("test-pepper");
    private readonly CapturingLogger _logger = new();
    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private static IConfiguration Configuration(params (string Name, string NationalId)[] seeds)
    {
        var entries = seeds.SelectMany(
            (seed, index) =>
                new KeyValuePair<string, string?>[]
                {
                    new($"CareRecipients:Seed:{index}:Name", seed.Name),
                    new($"CareRecipients:Seed:{index}:NationalId", seed.NationalId),
                }
        );

        return new ConfigurationBuilder().AddInMemoryCollection(entries).Build();
    }

    private static IConfiguration SeedGrantConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new KeyValuePair<string, string?>[]
                {
                    new("Kinship:SeedGrants:0:NationalId", "01010112345"),
                    new("Kinship:SeedGrants:0:DisplayName", "Fabian Quist"),
                }
            )
            .Build();

    private static IHostEnvironment Development()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        return environment;
    }

    private void Seed(IConfiguration configuration)
    {
        using var context = _factory.CreateContext();
        DbSeeder.SeedIfEmpty(
            context,
            _hasher,
            configuration,
            Development(),
            new FixedTimeProvider(Now)
        );
    }

    // SeedIfEmpty returns early on a database that already has rows, so a
    // database seeded before vedtak existed only ever gets them from here.
    [Fact]
    public void BackfillVedtak_ADatabaseSeededBeforeVedtakExisted_GetsVedtakAndTheConsentForThem()
    {
        Seed(SeedGrantConfiguration());
        GivenTheDatabasePredatesVedtak();

        using (var context = _factory.CreateContext())
        {
            DbSeeder.BackfillVedtak(context, new FixedTimeProvider(Now));
        }

        using var after = _factory.CreateContext();
        var careRecipients = after.CareRecipients.Select(c => c.Id).ToList();

        Assert.Equal(careRecipients.Count * 2, after.Vedtak.Count());
        Assert.All(
            careRecipients,
            careRecipientId =>
                Assert.Equal(
                    [ServiceType.Hjemmesykepleie, ServiceType.Fysioterapi],
                    // Ordered in C#: the column holds the enum name, so SQL would
                    // sort it alphabetically.
                    after
                        .Vedtak.Where(v => v.CareRecipientId == careRecipientId)
                        .Select(v => v.ServiceType)
                        .AsEnumerable()
                        .Order()
                )
        );

        var person = after.NextOfKin.Single();
        Assert.Equal(
            careRecipients.OrderBy(id => id),
            after
                .Consents.Where(c =>
                    c.NextOfKinId == person.Id && c.Category == DataCategory.Vedtak
                )
                .Select(c => c.CareRecipientId)
                .OrderBy(id => id)
        );
    }

    [Fact]
    public void BackfillVedtak_RunTwice_AddsNothingTheSecondTime()
    {
        Seed(Configuration(("Vigdis Quist", "13116900216")));
        GivenTheDatabasePredatesVedtak();

        using (var first = _factory.CreateContext())
        {
            DbSeeder.BackfillVedtak(first, new FixedTimeProvider(Now));
        }
        using (var second = _factory.CreateContext())
        {
            DbSeeder.BackfillVedtak(second, new FixedTimeProvider(Now));
        }

        using var after = _factory.CreateContext();
        Assert.Equal(2, after.Vedtak.Count());
        Assert.Equal(2, after.VedtakTasks.Select(t => t.VedtakId).Distinct().Count());
    }

    [Fact]
    public void BackfillVedtak_ANextOfKinWhoNeverConsentedToVisits_IsNotGivenAVedtakConsent()
    {
        Seed(SeedGrantConfiguration());
        GivenTheDatabasePredatesVedtak();

        int otherId;
        using (var context = _factory.CreateContext())
        {
            // Holds a consent, but for a category that is not the visit log.
            // Copying every open consent would hand this person the vedtak too.
            var other = new NextOfKin
            {
                NationalIdHash = _hasher.Hash("09090912345"),
                DisplayName = "Uten besøkssamtykke",
            };
            other.Consents.Add(
                new Consent
                {
                    CareRecipientId = context.CareRecipients.OrderBy(c => c.Id).First().Id,
                    Category = DataCategory.Medications,
                    ValidFrom = Now,
                }
            );
            context.NextOfKin.Add(other);
            context.SaveChanges();
            otherId = other.Id;
        }

        using (var context = _factory.CreateContext())
        {
            DbSeeder.BackfillVedtak(context, new FixedTimeProvider(Now));
        }

        using var after = _factory.CreateContext();
        Assert.Empty(
            after.Consents.Where(c => c.NextOfKinId == otherId && c.Category == DataCategory.Vedtak)
        );
        // The person who did consent to the visit log still gets theirs, so a backfill that
        // does nothing fails here.
        Assert.NotEmpty(
            after.Consents.Where(c => c.NextOfKinId != otherId && c.Category == DataCategory.Vedtak)
        );
    }

    [Fact]
    public void BackfillVedtak_AnEmptyDatabase_LeavesTheSeedingToSeedIfEmpty()
    {
        using (var context = _factory.CreateContext())
        {
            DbSeeder.BackfillVedtak(context, new FixedTimeProvider(Now));
        }

        using var after = _factory.CreateContext();
        Assert.Empty(after.Vedtak);
        Assert.Empty(after.CareRecipients);
    }

    // The shape before this step: care recipients, visits and consents, but no vedtak.
    private void GivenTheDatabasePredatesVedtak()
    {
        using var context = _factory.CreateContext();
        context.VedtakTasks.RemoveRange(context.VedtakTasks);
        context.Vedtak.RemoveRange(context.Vedtak);
        context.Consents.RemoveRange(
            context.Consents.Where(c => c.Category == DataCategory.Vedtak)
        );
        context.SaveChanges();
    }

    [Fact]
    public void TheSeedList_DecidesWhichCareRecipientsExist()
    {
        Seed(Configuration(("Vigdis Quist", "13116900216"), ("Tor Quist", "29099900157")));

        using var context = _factory.CreateContext();
        var recipients = context.CareRecipients.OrderBy(c => c.Name).ToList();

        Assert.Equal(["Tor Quist", "Vigdis Quist"], recipients.Select(c => c.Name));
        Assert.All(recipients, recipient => Assert.NotNull(recipient.NationalIdHash));
    }

    // The hash has to be the one sync computes from a snapshot, or the two
    // sides never meet.
    [Fact]
    public void ASeededRecipient_CarriesTheHashSyncWillLookHerUpBy()
    {
        Seed(Configuration(("Vigdis Quist", Snapshots.Vigdis.Value)));

        using var context = _factory.CreateContext();
        var vigdis = context.CareRecipients.Single();

        Assert.Equal(_hasher.Hash(Snapshots.Vigdis.HashInput), vigdis.NationalIdHash);
    }

    [Fact]
    public void AMisspelledSeedName_CreatesThatPersonRatherThanOrphaningTheFeed()
    {
        Seed(Configuration(("Vigdis Kvist", "13116900216")));

        using var context = _factory.CreateContext();
        var recipient = context.CareRecipients.Single();

        Assert.Equal("Vigdis Kvist", recipient.Name);
        Assert.NotNull(recipient.NationalIdHash);
    }

    [Fact]
    public void NoSeedList_StillLeavesTheDemoSomeoneToShow()
    {
        Seed(Configuration());

        using var context = _factory.CreateContext();
        var recipients = context.CareRecipients.ToList();

        Assert.Equal(2, recipients.Count);
        Assert.All(recipients, recipient => Assert.Null(recipient.NationalIdHash));
    }

    [Fact]
    public void TheStandInVedtak_AreDatedFromTheClockTheSeederIsGiven()
    {
        Seed(Configuration(("Vigdis Quist", "13116900216")));

        using var context = _factory.CreateContext();
        var today = NorwegianTime.DateOf(Now);

        Assert.Equal(
            [
                (ServiceType.Hjemmesykepleie, today.AddDays(-90), (DateOnly?)null),
                (ServiceType.Fysioterapi, today.AddDays(-30), today.AddDays(150)),
            ],
            context
                .Vedtak.AsEnumerable()
                .OrderBy(v => v.ServiceType)
                .Select(v => (v.ServiceType, v.ValidFrom, v.ValidTo))
        );
    }

    // Hand-seeded rows are orphans no source can reconcile, so they only stand
    // in where sync has no number to find the recipient by.
    [Fact]
    public void StandInVisits_AreSeeded_ForARecipientWithoutANumber()
    {
        Seed(Configuration());

        using var context = _factory.CreateContext();

        Assert.NotEmpty(context.Visits);
        Assert.All(context.Visits, visit => Assert.Equal(Origin.Synthetic, visit.Origin));
    }

    [Fact]
    public void NoStandInVisits_AreSeeded_ForARecipientWithANumber()
    {
        Seed(Configuration(("Vigdis Quist", "13116900216")));

        using var context = _factory.CreateContext();

        Assert.Empty(context.Visits);
        Assert.Empty(context.ChangeEvents);
    }

    // A database with no seed numbers syncs nothing, so without these the
    // inbox stays empty.
    [Fact]
    public void StandInVisits_ComeWithAnUnprocessedChangeEventEach()
    {
        Seed(Configuration());

        using var context = _factory.CreateContext();
        var visits = context.Visits.ToList();
        var events = context.ChangeEvents.ToList();

        Assert.Equal(visits.Count, events.Count);
        Assert.All(
            events,
            change =>
            {
                var visit = Assert.Single(visits, v => v.Id == change.VisitId);
                Assert.Equal(visit.CareRecipientId, change.CareRecipientId);
                Assert.Equal(visit.ScheduledAt, change.ScheduledAt);
                Assert.Equal(DataCategory.Visits, change.Category);
                Assert.Null(change.ProcessedAt);
                Assert.Equal(
                    visit.Status switch
                    {
                        VisitStatus.Completed => ChangeKind.Completed,
                        VisitStatus.Missed => ChangeKind.Missed,
                        _ => ChangeKind.Added,
                    },
                    change.Kind
                );
            }
        );
    }

    [Fact]
    public async Task EveryStandInEvent_ReachesTheSeededNextOfKin()
    {
        Seed(SeedGrantConfiguration());

        using (var context = _factory.CreateContext())
        {
            var fanOut = new NotificationFanOut(
                new EfChangeEventStore(context),
                new EfConsentRepository(context),
                new EfNotificationPreferenceRepository(context),
                new NotificationOptions { BatchSize = 100 },
                new FixedTimeProvider(Now)
            );

            await fanOut.DeliverPendingAsync(CancellationToken.None);
        }

        using var after = _factory.CreateContext();
        var nextOfKin = await after.NextOfKin.SingleAsync();
        var events = await after.ChangeEvents.ToListAsync();
        var notified = await after
            .Notifications.Where(n => n.NextOfKinId == nextOfKin.Id)
            .Select(n => n.ChangeEventId)
            .ToListAsync();

        Assert.NotEmpty(events);
        // seed has no past Added, so every event is news
        Assert.Equal(events.Select(e => e.Id).Order(), notified.Order());
    }

    // Without a seeded consent a fresh database would 403 the timeline. Medications
    // gets none: it has no endpoint, so a consent for it would open nothing.
    [Fact]
    public void ASeedGrant_ComesWithAVisitsAndAVedtakConsent_PerCareRecipient_AndNothingElse()
    {
        Seed(SeedGrantConfiguration());

        using var context = _factory.CreateContext();
        var person = context.NextOfKin.Single();
        var consents = context.Consents.Where(c => c.NextOfKinId == person.Id).ToList();

        Assert.Equal(2, context.CareRecipients.Count());
        Assert.Equal(4, consents.Count);
        Assert.Equal(
            context.CareRecipients.Select(c => c.Id).OrderBy(id => id),
            consents.Select(c => c.CareRecipientId).Distinct().OrderBy(id => id)
        );
        Assert.All(
            context.CareRecipients.Select(c => c.Id).ToList(),
            careRecipientId =>
                Assert.Equal(
                    [DataCategory.Visits, DataCategory.Vedtak],
                    consents
                        .Where(consent => consent.CareRecipientId == careRecipientId)
                        .Select(consent => consent.Category)
                        .Order()
                )
        );
        Assert.All(consents, consent => Assert.Null(consent.ValidTo));
    }

    [Fact]
    public void SeedingTwice_LeavesTheSecondRunAsANoOp()
    {
        var configuration = Configuration(("Vigdis Quist", "13116900216"));

        Seed(configuration);
        Seed(configuration);

        using var context = _factory.CreateContext();
        Assert.Equal(1, context.CareRecipients.Count());
    }

    // A database seeded before the column existed never gets a hash out of
    // SeedIfEmpty, which returns early once the table has rows. Without the
    // backfill, sync would resolve nothing against it for good.
    [Fact]
    public async Task TheBackfill_FillsAHashOnARecipientSeededBeforeTheColumnExisted()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(new CareRecipient { Name = "Vigdis Quist" });
            await seedContext.SaveChangesAsync();
        }

        using (var context = _factory.CreateContext())
        {
            DbSeeder.BackfillCareRecipientIdentities(
                context,
                _hasher,
                Configuration(("Vigdis Quist", Snapshots.Vigdis.Value)),
                _logger
            );
        }

        using var assertContext = _factory.CreateContext();
        var vigdis = await assertContext.CareRecipients.SingleAsync();

        Assert.Equal(_hasher.Hash(Snapshots.Vigdis.HashInput), vigdis.NationalIdHash);
    }

    [Fact]
    public async Task TheBackfill_LeavesARecipientWhoAlreadyHasANumberAlone()
    {
        var existing = _hasher.Hash(Snapshots.Tor.HashInput);

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(
                new CareRecipient { Name = "Vigdis Quist", NationalIdHash = existing }
            );
            await seedContext.SaveChangesAsync();
        }

        using (var context = _factory.CreateContext())
        {
            DbSeeder.BackfillCareRecipientIdentities(
                context,
                _hasher,
                Configuration(("Vigdis Quist", Snapshots.Vigdis.Value)),
                _logger
            );
        }

        using var assertContext = _factory.CreateContext();
        Assert.Equal(existing, (await assertContext.CareRecipients.SingleAsync()).NationalIdHash);
    }

    // The name match is the mismatch this backfill was named after, and
    // SeedIfEmpty has already returned, so a typo has no other sign.
    [Fact]
    public async Task ASeedNameMatchingNobody_IsWarnedAbout_ByName()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(new CareRecipient { Name = "Vigdis Quist" });
            await seedContext.SaveChangesAsync();
        }

        using (var context = _factory.CreateContext())
        {
            DbSeeder.BackfillCareRecipientIdentities(
                context,
                _hasher,
                Configuration(("Vigdis Kvist", "13116900216")),
                _logger
            );
        }

        var warning = Assert.Single(_logger.Warnings);

        Assert.Contains("Vigdis Kvist", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("13116900216", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASeedNameThatMatched_IsNotWarnedAbout()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(new CareRecipient { Name = "Vigdis Quist" });
            await seedContext.SaveChangesAsync();
        }

        using (var context = _factory.CreateContext())
        {
            DbSeeder.BackfillCareRecipientIdentities(
                context,
                _hasher,
                Configuration(("Vigdis Quist", Snapshots.Vigdis.Value)),
                _logger
            );
        }

        Assert.Empty(_logger.Warnings);
    }

    // A fresh database is SeedIfEmpty's job, and it writes the hashes itself.
    // Warning there would fire on every clean start.
    [Fact]
    public void AnEmptyDatabase_IsNotWarnedAbout()
    {
        using (var context = _factory.CreateContext())
        {
            DbSeeder.BackfillCareRecipientIdentities(
                context,
                _hasher,
                Configuration(("Vigdis Quist", "13116900216")),
                _logger
            );
        }

        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public async Task TheBackfill_SkipsARecipientNoSeedNames()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(new CareRecipient { Name = "Kari Nordmann" });
            await seedContext.SaveChangesAsync();
        }

        using (var context = _factory.CreateContext())
        {
            DbSeeder.BackfillCareRecipientIdentities(
                context,
                _hasher,
                Configuration(("Vigdis Quist", "13116900216")),
                _logger
            );
        }

        using var assertContext = _factory.CreateContext();
        Assert.Null((await assertContext.CareRecipients.SingleAsync()).NationalIdHash);
    }
}
