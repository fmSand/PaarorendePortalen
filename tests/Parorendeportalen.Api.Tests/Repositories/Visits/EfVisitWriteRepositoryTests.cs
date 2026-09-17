using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models.Kinship;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Repositories.Visits;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories.Visits;

// Visibility filter and xmin concurrency both need Postgres to mean anything.
[Collection(PostgresCollection.Name)]
public class EfVisitWriteRepositoryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task GetByCareRecipientIdAsync_HidesAnotherNextOfKinsPrivateEntry()
    {
        var (careRecipientId, fabian, siri) = await SeedPeopleAsync();

        await SeedVisitsAsync(
            careRecipientId,
            AnEntry(careRecipientId, fabian, Visibility.Private, "fabian private"),
            AnEntry(careRecipientId, fabian, Visibility.Shared, "fabian shared"),
            AnEntry(careRecipientId, siri, Visibility.Private, "siri private"),
            ASourceVisit(careRecipientId, "from the municipality")
        );

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var (items, totalCount) = await sut.GetByCareRecipientIdAsync(
            careRecipientId,
            fabian,
            from: null,
            to: null,
            pageNumber: 1,
            pageSize: 20,
            CancellationToken.None
        );

        Assert.Equal(
            ["fabian private", "fabian shared", "from the municipality"],
            items.Select(v => v.Notes!).Order()
        );
        // A leaked row shows up in the client's page count even off the current page.
        Assert.Equal(3, totalCount);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_ForAnotherNextOfKinsPrivateEntry()
    {
        var (careRecipientId, fabian, siri) = await SeedPeopleAsync();
        var siris = AnEntry(careRecipientId, siri, Visibility.Private, "siri private");

        await SeedVisitsAsync(careRecipientId, siris);

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var result = await sut.GetByIdAsync(
            siris.Id,
            careRecipientId,
            fabian,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task GetInRangeAsync_HidesAnotherNextOfKinsPrivateEntry()
    {
        var (careRecipientId, fabian, siri) = await SeedPeopleAsync();
        var (start, end) = Parorendeportalen.Api.Services.NorwegianTime.BoundsOf(
            new DateOnly(2026, 9, 7)
        );

        var mine = AnEntry(careRecipientId, fabian, Visibility.Private, "mine");
        mine.ScheduledAt = start.AddHours(9).ToUniversalTime();
        var theirs = AnEntry(careRecipientId, siri, Visibility.Private, "theirs");
        theirs.ScheduledAt = start.AddHours(10).ToUniversalTime();

        await SeedVisitsAsync(careRecipientId, mine, theirs);

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);

        var result = await sut.GetInRangeAsync(
            careRecipientId,
            fabian,
            start,
            end,
            CancellationToken.None
        );

        Assert.Equal(["mine"], result.Select(v => v.Notes!));
    }

    // Loads a row someone else already changed; checking it against itself would discard their edit.
    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenTheRowMovedOnSinceTheCallerReadIt()
    {
        var (careRecipientId, fabian, _) = await SeedPeopleAsync();
        var entry = AnEntry(careRecipientId, fabian, Visibility.Shared, "original");
        await SeedVisitsAsync(careRecipientId, entry);

        var versionTheClientHolds = await VersionOfAsync(entry.Id);
        await ChangeElsewhereAsync(entry.Id, "changed by someone else");

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);
        var loaded = await sut.GetForWriteAsync(entry.Id, careRecipientId, CancellationToken.None);
        loaded!.Notes = "changed by me";

        Assert.False(await sut.UpdateAsync(loaded, versionTheClientHolds, CancellationToken.None));

        using var verifyContext = _factory.CreateContext();
        var stored = await verifyContext.Visits.SingleAsync(v => v.Id == entry.Id);
        Assert.Equal("changed by someone else", stored.Notes);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsTrue_AndStampsANewVersion_WhenNobodyElseWrote()
    {
        var (careRecipientId, fabian, _) = await SeedPeopleAsync();
        var entry = AnEntry(careRecipientId, fabian, Visibility.Shared, "original");
        await SeedVisitsAsync(careRecipientId, entry);

        var versionTheClientHolds = await VersionOfAsync(entry.Id);

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);
        var loaded = await sut.GetForWriteAsync(entry.Id, careRecipientId, CancellationToken.None);
        loaded!.Notes = "changed by me";

        Assert.True(await sut.UpdateAsync(loaded, versionTheClientHolds, CancellationToken.None));

        // A version that did not move would let the next writer overwrite blind.
        Assert.NotEqual(versionTheClientHolds, loaded.Version);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_AndKeepsTheRow_WhenTheVersionIsStale()
    {
        var (careRecipientId, fabian, _) = await SeedPeopleAsync();
        var entry = AnEntry(careRecipientId, fabian, Visibility.Shared, "original");
        await SeedVisitsAsync(careRecipientId, entry);

        var versionTheClientHolds = await VersionOfAsync(entry.Id);
        await ChangeElsewhereAsync(entry.Id, "changed by someone else");

        using var context = _factory.CreateContext();
        var sut = new EfVisitRepository(context);
        var loaded = await sut.GetForWriteAsync(entry.Id, careRecipientId, CancellationToken.None);

        Assert.False(await sut.DeleteAsync(loaded!, versionTheClientHolds, CancellationToken.None));

        using var verifyContext = _factory.CreateContext();
        Assert.True(await verifyContext.Visits.AnyAsync(v => v.Id == entry.Id));
    }

    private async Task<uint> VersionOfAsync(int visitId)
    {
        using var context = _factory.CreateContext();

        return await context
            .Visits.AsNoTracking()
            .Where(v => v.Id == visitId)
            .Select(v => v.Version)
            .SingleAsync();
    }

    private async Task ChangeElsewhereAsync(int visitId, string notes)
    {
        using var context = _factory.CreateContext();
        var theirs = await context.Visits.SingleAsync(v => v.Id == visitId);
        theirs.Notes = notes;
        await context.SaveChangesAsync();
    }

    private async Task<(int CareRecipientId, int Fabian, int Siri)> SeedPeopleAsync()
    {
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        var fabian = new NextOfKin
        {
            NationalIdHash = new string('a', 64),
            DisplayName = "Fabian Quist",
        };
        var siri = new NextOfKin
        {
            NationalIdHash = new string('b', 64),
            DisplayName = "Siri Quist",
        };

        using var context = _factory.CreateContext();
        context.CareRecipients.Add(vigdis);
        context.NextOfKin.AddRange(fabian, siri);
        await context.SaveChangesAsync();

        return (vigdis.Id, fabian.Id, siri.Id);
    }

    private async Task SeedVisitsAsync(int careRecipientId, params Visit[] visits)
    {
        using var context = _factory.CreateContext();
        foreach (var visit in visits)
        {
            visit.CareRecipientId = careRecipientId;
        }

        context.Visits.AddRange(visits);
        await context.SaveChangesAsync();
    }

    private static Visit AnEntry(
        int careRecipientId,
        int author,
        Visibility visibility,
        string notes
    ) =>
        new()
        {
            CareRecipientId = careRecipientId,
            ScheduledAt = DateTimeOffset.UtcNow,
            Status = VisitStatus.Planned,
            Origin = Origin.Portal,
            CreatedByNextOfKinId = author,
            Visibility = visibility,
            Notes = notes,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static Visit ASourceVisit(int careRecipientId, string notes) =>
        new()
        {
            CareRecipientId = careRecipientId,
            ScheduledAt = DateTimeOffset.UtcNow,
            Status = VisitStatus.Planned,
            Origin = Origin.Synthetic,
            ExternalId = "source-0001",
            Notes = notes,
        };
}
