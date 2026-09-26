using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models.Kinship;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Repositories.Visits;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories.Visits;

[Collection(PostgresCollection.Name)]
public class EfVisitCommentRepositoryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _factory = null!;

    private int _visitId;
    private int _otherVisitId;
    private int _fabian;
    private int _siri;

    public async Task InitializeAsync()
    {
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

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
        var visit = ASourceVisit(vigdis, "source-0001");
        var otherVisit = ASourceVisit(vigdis, "source-0002");

        using var context = _factory.CreateContext();
        context.CareRecipients.Add(vigdis);
        context.NextOfKin.AddRange(fabian, siri);
        context.Visits.AddRange(visit, otherVisit);
        await context.SaveChangesAsync();

        _visitId = visit.Id;
        _otherVisitId = otherVisit.Id;
        _fabian = fabian.Id;
        _siri = siri.Id;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task GetByVisitIdAsync_ReturnsSharedAndOwnComments_OldestFirst()
    {
        var start = new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);
        await SeedCommentsAsync(
            AComment(_visitId, _siri, Visibility.Shared, "siri shared", start),
            AComment(_visitId, _fabian, Visibility.Private, "fabian private", start.AddHours(1)),
            AComment(_visitId, _siri, Visibility.Private, "siri private", start.AddHours(2)),
            AComment(_otherVisitId, _fabian, Visibility.Shared, "other visit", start.AddHours(3))
        );

        using var context = _factory.CreateContext();
        var sut = new EfVisitCommentRepository(context);

        var thread = await sut.GetByVisitIdAsync(_visitId, _fabian, CancellationToken.None);

        Assert.Equal(["siri shared", "fabian private"], thread.Select(c => c.Body));
        Assert.Equal(["Siri Quist", "Fabian Quist"], thread.Select(c => c.Author.DisplayName));
    }

    [Fact]
    public async Task GetByVisitIdAsync_ReturnsTheSameCommentsAsVisitCommentIsVisibleTo()
    {
        var at = DateTimeOffset.UtcNow;
        VisitComment[] all =
        [
            AComment(_visitId, _fabian, Visibility.Private, "fabian private", at),
            AComment(_visitId, _fabian, Visibility.Shared, "fabian shared", at),
            AComment(_visitId, _siri, Visibility.Private, "siri private", at),
            AComment(_visitId, _siri, Visibility.Shared, "siri shared", at),
        ];
        await SeedCommentsAsync(all);

        using var context = _factory.CreateContext();
        var sut = new EfVisitCommentRepository(context);

        foreach (var viewer in new[] { _fabian, _siri })
        {
            var thread = await sut.GetByVisitIdAsync(_visitId, viewer, CancellationToken.None);

            Assert.Equal(
                all.Where(c => c.IsVisibleTo(viewer)).Select(c => c.Body).Order(),
                thread.Select(c => c.Body).Order()
            );
        }
    }

    // Loads a row someone else already changed
    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenTheCommentMovedOnSinceTheCallerReadIt()
    {
        var comment = AComment(
            _visitId,
            _fabian,
            Visibility.Shared,
            "original",
            DateTimeOffset.UtcNow
        );
        await SeedCommentsAsync(comment);

        var versionTheClientHolds = await VersionOfAsync(comment.Id);

        using (var otherContext = _factory.CreateContext())
        {
            var theirs = await otherContext.VisitComments.SingleAsync(c => c.Id == comment.Id);
            theirs.Body = "edited elsewhere";
            await otherContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var sut = new EfVisitCommentRepository(context);
        var loaded = await sut.GetForWriteAsync(comment.Id, _visitId, CancellationToken.None);
        loaded!.Body = "edited by me";

        Assert.False(await sut.UpdateAsync(loaded, versionTheClientHolds, CancellationToken.None));

        using var verifyContext = _factory.CreateContext();
        var stored = await verifyContext.VisitComments.SingleAsync(c => c.Id == comment.Id);
        Assert.Equal("edited elsewhere", stored.Body);
    }

    [Fact]
    public async Task DeletingAVisit_TakesItsThreadWithIt()
    {
        var comment = AComment(
            _visitId,
            _fabian,
            Visibility.Shared,
            "on a visit about to go",
            DateTimeOffset.UtcNow
        );
        await SeedCommentsAsync(comment);

        using (var deleteContext = _factory.CreateContext())
        {
            var visit = await deleteContext.Visits.SingleAsync(v => v.Id == _visitId);
            deleteContext.Visits.Remove(visit);
            await deleteContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();

        // Comments are deleted with their visit, so none is left holding text about a visit that is gone.
        Assert.False(await context.VisitComments.AnyAsync(c => c.Id == comment.Id));
    }

    private async Task<uint> VersionOfAsync(int commentId)
    {
        using var context = _factory.CreateContext();

        return await context
            .VisitComments.AsNoTracking()
            .Where(c => c.Id == commentId)
            .Select(c => c.Version)
            .SingleAsync();
    }

    private async Task SeedCommentsAsync(params VisitComment[] comments)
    {
        using var context = _factory.CreateContext();
        context.VisitComments.AddRange(comments);
        await context.SaveChangesAsync();
    }

    private static VisitComment AComment(
        int visitId,
        int author,
        Visibility visibility,
        string body,
        DateTimeOffset createdAt
    ) =>
        new()
        {
            VisitId = visitId,
            AuthorNextOfKinId = author,
            Body = body,
            Visibility = visibility,
            CreatedAt = createdAt,
        };

    private static Visit ASourceVisit(CareRecipient careRecipient, string externalId) =>
        new()
        {
            CareRecipient = careRecipient,
            ScheduledAt = DateTimeOffset.UtcNow,
            Status = VisitStatus.Planned,
            Origin = Origin.Synthetic,
            ExternalId = externalId,
        };
}
