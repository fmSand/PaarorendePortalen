using System.Globalization;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Kinship;
using Parorendeportalen.Api.Models.Planning;
using Parorendeportalen.Api.Repositories.Planning;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories.Planning;

[Collection(PostgresCollection.Name)]
public class EfVedtakRepositoryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    // A Wednesday, so a rule naming weekdays covers it.
    private static readonly DateOnly Wednesday = new(2026, 9, 9);

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenTheVedtakBelongsToAnotherCareRecipient()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var ola = new CareRecipient { Name = "Ola Nordmann" };
        var olasVedtak = AVedtak(ola, "Hjemmesykepleie for Ola");

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.AddRange(kari, ola);
            seedContext.Vedtak.Add(olasVedtak);
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVedtakRepository(context);

        Assert.Null(await sut.GetByIdAsync(olasVedtak.Id, kari.Id, CancellationToken.None));

        // The same id works under its own care recipient, so the null above comes from the scope.
        var olasOwn = await sut.GetByIdAsync(olasVedtak.Id, ola.Id, CancellationToken.None);
        Assert.NotNull(olasOwn);
        Assert.Equal("Hjemmesykepleie for Ola", olasOwn.Title);
        Assert.Equal(ola.Id, olasOwn.CareRecipientId);
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_ReturnsOnlyThatPersonsVedtak_NewestValidFromFirst()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var ola = new CareRecipient { Name = "Ola Nordmann" };

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.AddRange(kari, ola);
            // Seeded out of order, so the order asserted is the query's.
            seedContext.Vedtak.AddRange(
                AVedtak(kari, "oldest", validFrom: new DateOnly(2026, 1, 1)),
                AVedtak(kari, "newest", validFrom: new DateOnly(2026, 6, 1)),
                AVedtak(
                    kari,
                    "revoked",
                    validFrom: new DateOnly(2026, 4, 1),
                    status: VedtakStatus.Revoked
                ),
                AVedtak(kari, "middle", validFrom: new DateOnly(2026, 3, 1)),
                AVedtak(ola, "ola's own", validFrom: new DateOnly(2026, 5, 1))
            );
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVedtakRepository(context);

        var result = await sut.GetByCareRecipientIdAsync(kari.Id, CancellationToken.None);

        // The revoked one stays in the list, so a next-of-kin can see it was revoked.
        Assert.Equal(["newest", "revoked", "middle", "oldest"], result.Select(v => v.Title));
        Assert.All(result, v => Assert.Equal(kari.Id, v.CareRecipientId));
    }

    [Fact]
    public async Task Tasks_ComeBackOrderedBySequence_NotByTheOrderTheyWereWritten()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var vedtak = AVedtak(kari, "Hjemmesykepleie");
        // Written in an order that disagrees with Sequence, so row order can't be the answer.
        vedtak.Tasks.Add(new VedtakTask { Description = "Kveldsstell", Sequence = 3 });
        vedtak.Tasks.Add(new VedtakTask { Description = "Morgenstell", Sequence = 1 });
        vedtak.Tasks.Add(new VedtakTask { Description = "Medisiner", Sequence = 2 });

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.Vedtak.Add(vedtak);
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVedtakRepository(context);

        var byId = await sut.GetByIdAsync(vedtak.Id, kari.Id, CancellationToken.None);
        var listed = await sut.GetByCareRecipientIdAsync(kari.Id, CancellationToken.None);

        Assert.NotNull(byId);
        Assert.Equal(
            ["Morgenstell", "Medisiner", "Kveldsstell"],
            byId.Tasks.Select(task => task.Description)
        );
        Assert.Equal([1, 2, 3], byId.Tasks.Select(task => task.Sequence));
        Assert.Equal(
            ["Morgenstell", "Medisiner", "Kveldsstell"],
            Assert.Single(listed).Tasks.Select(task => task.Description)
        );
    }

    [Fact]
    public async Task Recurrence_RoundTripsThroughPostgres_WithItsDaysAndTimesPerDay()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var vedtak = AVedtak(
            kari,
            "Hjemmesykepleie x3/dag man, ons, fre",
            days: Weekdays.Monday | Weekdays.Wednesday | Weekdays.Friday,
            timesPerDay: 3
        );

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.Vedtak.Add(vedtak);
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVedtakRepository(context);

        var result = await sut.GetByIdAsync(vedtak.Id, kari.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(
            Weekdays.Monday | Weekdays.Wednesday | Weekdays.Friday,
            result.Recurrence.Days
        );
        Assert.Equal(3, result.Recurrence.TimesPerDay);
        // Still a flag set, so a single day can be tested against it the way the day plan does.
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
            result.Recurrence.Days.ToDays()
        );
        Assert.True(result.Recurrence.Covers(Wednesday));
        Assert.False(result.Recurrence.Covers(Wednesday.AddDays(1)));
    }

    // EfVedtakRepository states the in-force rule in SQL, Vedtak.IsInForceOn in C#; this is what stops the two drifting apart.
    [Fact]
    public async Task GetInForceOnAsync_ReturnsTheSameRowsAsVedtakIsInForceOn()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var ola = new CareRecipient { Name = "Ola Nordmann" };
        var date = Wednesday;

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.AddRange(kari, ola);
            seedContext.Vedtak.AddRange(
                AVedtak(kari, "active-open-ended", validFrom: date.AddDays(-30)),
                AVedtak(
                    kari,
                    "active-spanning",
                    validFrom: date.AddDays(-30),
                    validTo: date.AddDays(30)
                ),
                AVedtak(kari, "active-starts-today", validFrom: date, validTo: date.AddDays(30)),
                AVedtak(kari, "active-ends-today", validFrom: date.AddDays(-30), validTo: date),
                AVedtak(kari, "active-today-only", validFrom: date, validTo: date),
                AVedtak(kari, "active-starts-tomorrow", validFrom: date.AddDays(1)),
                AVedtak(
                    kari,
                    "active-ended-yesterday",
                    validFrom: date.AddDays(-30),
                    validTo: date.AddDays(-1)
                ),
                AVedtak(kari, "draft", validFrom: date.AddDays(-30), status: VedtakStatus.Draft),
                AVedtak(kari, "on-hold", validFrom: date.AddDays(-30), status: VedtakStatus.OnHold),
                AVedtak(
                    kari,
                    "revoked",
                    validFrom: date.AddDays(-30),
                    status: VedtakStatus.Revoked
                ),
                AVedtak(
                    kari,
                    "completed",
                    validFrom: date.AddDays(-30),
                    status: VedtakStatus.Completed
                ),
                AVedtak(ola, "ola-active-spanning", validFrom: date.AddDays(-30))
            );
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVedtakRepository(context);

        var all = await sut.GetByCareRecipientIdAsync(kari.Id, CancellationToken.None);
        Assert.Equal(11, all.Count);

        // Written out as well as compared, so a rule wrong in both places still fails here.
        var onTheDay = await sut.GetInForceOnAsync(kari.Id, date, CancellationToken.None);
        Assert.Equal(
            [
                "active-ends-today",
                "active-open-ended",
                "active-spanning",
                "active-starts-today",
                "active-today-only",
            ],
            onTheDay.Select(v => v.Title).Order(StringComparer.Ordinal)
        );
        Assert.DoesNotContain("ola-active-spanning", onTheDay.Select(v => v.Title));

        var fromSql = new List<string>();
        var fromTheModel = new List<string>();
        foreach (
            var probe in (DateOnly[])[date.AddDays(-1), date, date.AddDays(1), date.AddDays(31)]
        )
        {
            var rows = await sut.GetInForceOnAsync(kari.Id, probe, CancellationToken.None);
            fromSql.AddRange(Label(probe, rows.Select(v => v.Title)));
            fromTheModel.AddRange(
                Label(probe, all.Where(v => v.IsInForceOn(probe)).Select(v => v.Title))
            );
        }

        Assert.Equal(fromTheModel, fromSql);
    }

    private static IEnumerable<string> Label(DateOnly date, IEnumerable<string> titles) =>
        titles
            .Select(title => $"{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {title}")
            .Order(StringComparer.Ordinal);

    private static Vedtak AVedtak(
        CareRecipient careRecipient,
        string title,
        ServiceType serviceType = ServiceType.Hjemmesykepleie,
        Weekdays days = Weekdays.EveryDay,
        int timesPerDay = 1,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        VedtakStatus status = VedtakStatus.Active
    ) =>
        new()
        {
            CareRecipient = careRecipient,
            ServiceType = serviceType,
            Title = title,
            Recurrence = new RecurrenceRule { Days = days, TimesPerDay = timesPerDay },
            ValidFrom = validFrom ?? new DateOnly(2026, 1, 1),
            ValidTo = validTo,
            Status = status,
        };
}
