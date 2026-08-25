using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories;

public class EfVisitRepositoryTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetByCareRecipientIdAsync_ReturnsOnlyVisitsForThatCareRecipient()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var ola = new CareRecipient { Name = "Ola Nordmann" };

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.AddRange(kari, ola);
            seedContext.Visits.AddRange(
                new Visit { CareRecipient = kari, ScheduledAt = DateTimeOffset.UtcNow, Status = VisitStatus.Planned },
                new Visit { CareRecipient = kari, ScheduledAt = DateTimeOffset.UtcNow.AddDays(1), Status = VisitStatus.Planned },
                new Visit { CareRecipient = ola, ScheduledAt = DateTimeOffset.UtcNow, Status = VisitStatus.Planned });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var (items, totalCount) = await sut.GetByCareRecipientIdAsync(
            kari.Id, from: null, to: null, pageNumber: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Equal(2, totalCount);
        Assert.All(items, v => Assert.Equal(kari.Id, v.CareRecipientId));
        Assert.All(items, v => Assert.Equal("Kari Nordmann", v.CareRecipient.Name));
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_ReturnsVisitsOrderedByScheduledAt()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var earliest = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
        var middle = new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero);
        var latest = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.Visits.AddRange(
                new Visit { CareRecipient = kari, ScheduledAt = latest, Status = VisitStatus.Planned, Notes = "latest" },
                new Visit { CareRecipient = kari, ScheduledAt = earliest, Status = VisitStatus.Completed, Notes = "earliest" },
                new Visit { CareRecipient = kari, ScheduledAt = middle, Status = VisitStatus.Missed, Notes = "middle" });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var (items, _) = await sut.GetByCareRecipientIdAsync(
            kari.Id, from: null, to: null, pageNumber: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(["earliest", "middle", "latest"], items.Select(v => v.Notes));
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_ReturnsEmptyList_WhenCareRecipientHasNoVisits()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var (items, totalCount) = await sut.GetByCareRecipientIdAsync(
            kari.Id, from: null, to: null, pageNumber: 1, pageSize: 20, CancellationToken.None);

        Assert.Empty(items);
        Assert.Equal(0, totalCount);
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_FiltersByFromAndToInclusive()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var beforeRange = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);
        var rangeStart = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
        var insideRange = new DateTimeOffset(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);
        var afterRange = new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero);

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.Visits.AddRange(
                new Visit { CareRecipient = kari, ScheduledAt = beforeRange, Status = VisitStatus.Planned, Notes = "before" },
                new Visit { CareRecipient = kari, ScheduledAt = rangeStart, Status = VisitStatus.Planned, Notes = "at-start" },
                new Visit { CareRecipient = kari, ScheduledAt = insideRange, Status = VisitStatus.Planned, Notes = "inside" },
                new Visit { CareRecipient = kari, ScheduledAt = rangeEnd, Status = VisitStatus.Planned, Notes = "at-end" },
                new Visit { CareRecipient = kari, ScheduledAt = afterRange, Status = VisitStatus.Planned, Notes = "after" });
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var (items, totalCount) = await sut.GetByCareRecipientIdAsync(
            kari.Id, from: rangeStart, to: rangeEnd, pageNumber: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(3, totalCount);
        Assert.Equal(["at-start", "inside", "at-end"], items.Select(v => v.Notes));
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_PagesResults_AndReportsUnpagedTotalCount()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var visits = Enumerable.Range(0, 5)
            .Select(i => new Visit
            {
                CareRecipient = kari,
                ScheduledAt = new DateTimeOffset(2026, 8, 1 + i, 8, 0, 0, TimeSpan.Zero),
                Status = VisitStatus.Planned,
                Notes = $"visit-{i}"
            })
            .ToList();

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.Visits.AddRange(visits);
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var (page2Items, totalCount) = await sut.GetByCareRecipientIdAsync(
            kari.Id, from: null, to: null, pageNumber: 2, pageSize: 2, CancellationToken.None);

        Assert.Equal(["visit-2", "visit-3"], page2Items.Select(v => v.Notes));
        Assert.Equal(5, totalCount);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsVisit_WhenItBelongsToTheCareRecipient()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var visit = new Visit
        {
            CareRecipient = kari,
            ScheduledAt = DateTimeOffset.UtcNow,
            Status = VisitStatus.Completed,
            Notes = "kari's visit"
        };

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var result = await sut.GetByIdAsync(visit.Id, kari.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(visit.Id, result.Id);
        Assert.Equal("kari's visit", result.Notes);
        Assert.Equal("Kari Nordmann", result.CareRecipient.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenVisitBelongsToADifferentCareRecipient()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        var ola = new CareRecipient { Name = "Ola Nordmann" };
        var olasVisit = new Visit { CareRecipient = ola, ScheduledAt = DateTimeOffset.UtcNow, Status = VisitStatus.Planned };

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.AddRange(kari, ola);
            seedContext.Visits.Add(olasVisit);
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var result = await sut.GetByIdAsync(olasVisit.Id, kari.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNoVisitHasThatId()
    {
        var kari = new CareRecipient { Name = "Kari Nordmann" };
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(kari);
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var result = await sut.GetByIdAsync(9999, kari.Id, CancellationToken.None);

        Assert.Null(result);
    }
}
