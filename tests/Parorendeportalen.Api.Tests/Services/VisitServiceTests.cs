using NSubstitute;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Services;

public class VisitServiceTests
{
    private const int Viewer = 7;

    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly IVisitRepository _repository = Substitute.For<IVisitRepository>();
    private readonly ICurrentNextOfKinAccessor _currentNextOfKin =
        Substitute.For<ICurrentNextOfKinAccessor>();
    private readonly VisitService _sut;

    public VisitServiceTests()
    {
        _currentNextOfKin
            .GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new CurrentNextOfKin(Viewer, [1, 3, 7, 99]));

        _sut = new VisitService(_repository, _currentNextOfKin, new FixedTimeProvider(Now));
    }

    private static Visit CreateVisit(
        int id,
        int careRecipientId,
        string careRecipientName,
        DateTimeOffset scheduledAt,
        VisitStatus status = VisitStatus.Planned,
        DateTimeOffset? actualAt = null,
        string? caregiverName = null,
        string? notes = null
    ) =>
        new()
        {
            Id = id,
            CareRecipientId = careRecipientId,
            CareRecipient = new CareRecipient { Id = careRecipientId, Name = careRecipientName },
            ScheduledAt = scheduledAt,
            ActualAt = actualAt,
            Status = status,
            CaregiverName = caregiverName,
            Notes = notes,
        };

    private void SetupRepository(int careRecipientId, IReadOnlyList<Visit> items, int totalCount) =>
        _repository
            .GetByCareRecipientIdAsync(
                careRecipientId,
                Viewer,
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((items, totalCount));

    [Fact]
    public async Task GetByCareRecipientIdAsync_ReturnsMappedVisits_ForRequestedCareRecipientId()
    {
        var scheduledAt = new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);
        var actualAt = scheduledAt.AddMinutes(5);
        var visits = new List<Visit>
        {
            CreateVisit(
                id: 42,
                careRecipientId: 1,
                careRecipientName: "Kari Nordmann",
                scheduledAt: scheduledAt,
                status: VisitStatus.Completed,
                actualAt: actualAt,
                caregiverName: "Hjemmetjenesten Oslo",
                notes: "Morgenstell og medisiner gitt."
            ),
        };
        SetupRepository(1, visits, totalCount: 1);

        var result = await _sut.GetByCareRecipientIdAsync(
            1,
            from: null,
            to: null,
            pageNumber: 1,
            pageSize: 20,
            CancellationToken.None
        );

        var response = Assert.Single(result.Items);
        Assert.Equal(42, response.Id);
        Assert.Equal(1, response.CareRecipientId);
        Assert.Equal("Kari Nordmann", response.CareRecipientName);
        Assert.Equal(scheduledAt, response.ScheduledAt);
        Assert.Equal(actualAt, response.ActualAt);
        Assert.Equal(VisitStatus.Completed, response.Status);
        Assert.Equal("Hjemmetjenesten Oslo", response.CaregiverName);
        Assert.Equal("Morgenstell og medisiner gitt.", response.Notes);
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_ReturnsEmptyList_WhenRepositoryHasNoVisits()
    {
        SetupRepository(7, new List<Visit>(), totalCount: 0);

        var result = await _sut.GetByCareRecipientIdAsync(
            7,
            from: null,
            to: null,
            pageNumber: 1,
            pageSize: 20,
            CancellationToken.None
        );

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_PassesRequestedFilterAndPagingArguments_ToRepository()
    {
        using var cts = new CancellationTokenSource();
        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        SetupRepository(99, new List<Visit>(), totalCount: 0);

        await _sut.GetByCareRecipientIdAsync(99, from, to, pageNumber: 2, pageSize: 10, cts.Token);

        await _repository
            .Received(1)
            .GetByCareRecipientIdAsync(99, Viewer, from, to, 2, 10, cts.Token);
        await _repository
            .DidNotReceive()
            .GetByCareRecipientIdAsync(
                Arg.Is<int>(id => id != 99),
                Arg.Any<int>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_ReturnsVisitsInRepositoryOrder()
    {
        var earlier = CreateVisit(
            1,
            1,
            "Kari Nordmann",
            new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero)
        );
        var later = CreateVisit(
            2,
            1,
            "Kari Nordmann",
            new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero)
        );
        SetupRepository(1, new List<Visit> { earlier, later }, totalCount: 2);

        var result = await _sut.GetByCareRecipientIdAsync(
            1,
            from: null,
            to: null,
            pageNumber: 1,
            pageSize: 20,
            CancellationToken.None
        );

        Assert.Equal([1, 2], result.Items.Select(v => v.Id));
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_MapsPagingMetadata_FromRequestAndRepositoryTotalCount()
    {
        SetupRepository(1, new List<Visit>(), totalCount: 47);

        var result = await _sut.GetByCareRecipientIdAsync(
            1,
            from: null,
            to: null,
            pageNumber: 3,
            pageSize: 20,
            CancellationToken.None
        );

        Assert.Equal(3, result.PageNumber);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(47, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsMappedVisit_WhenRepositoryReturnsIt()
    {
        var scheduledAt = new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);
        var visit = CreateVisit(
            id: 42,
            careRecipientId: 1,
            careRecipientName: "Kari Nordmann",
            scheduledAt: scheduledAt,
            status: VisitStatus.Completed,
            actualAt: scheduledAt.AddMinutes(5),
            caregiverName: "Hjemmetjenesten Oslo",
            notes: "Morgenstell og medisiner gitt."
        );
        _repository.GetByIdAsync(42, 1, Viewer, Arg.Any<CancellationToken>()).Returns(visit);

        var response = await _sut.GetByIdAsync(42, 1, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(42, response.Id);
        Assert.Equal(1, response.CareRecipientId);
        Assert.Equal("Kari Nordmann", response.CareRecipientName);
        Assert.Equal(VisitStatus.Completed, response.Status);
        Assert.Equal("Morgenstell og medisiner gitt.", response.Notes);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenRepositoryFindsNothing()
    {
        _repository
            .GetByIdAsync(999, 1, Viewer, Arg.Any<CancellationToken>())
            .Returns((Visit?)null);

        var response = await _sut.GetByIdAsync(999, 1, CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task GetByIdAsync_PassesIdAndCareRecipientIdScope_ToRepository()
    {
        using var cts = new CancellationTokenSource();
        _repository
            .GetByIdAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((Visit?)null);

        await _sut.GetByIdAsync(id: 5, careRecipientId: 3, cts.Token);

        await _repository.Received(1).GetByIdAsync(5, 3, Viewer, cts.Token);
        await _repository
            .DidNotReceive()
            .GetByIdAsync(
                Arg.Any<int>(),
                Arg.Is<int>(careRecipientId => careRecipientId != 3),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task CreateAsync_StampsTheEntryAsThisNextOfKinsOwn()
    {
        Visit? saved = null;
        _repository
            .AddAsync(Arg.Any<Visit>(), Arg.Any<CancellationToken>())
            .Returns(call => saved = call.Arg<Visit>());
        _repository
            .GetByIdAsync(Arg.Any<int>(), 1, Viewer, Arg.Any<CancellationToken>())
            .Returns(_ => WithCareRecipient(saved!));

        await _sut.CreateAsync(
            new CreateVisitRequest
            {
                CareRecipientId = 1,
                ScheduledAt = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
                Title = "Legetime",
                Visibility = Visibility.Shared,
            },
            CancellationToken.None
        );

        Assert.NotNull(saved);
        // Portal, so sync never reconciles it away; the author, so only they may edit it.
        Assert.Equal(Origin.Portal, saved.Origin);
        Assert.Equal(Viewer, saved.CreatedByNextOfKinId);
        Assert.Equal(Visibility.Shared, saved.Visibility);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Equal(VisitStatus.Planned, saved.Status);
        Assert.Null(saved.ServiceType);
    }

    [Theory]
    [InlineData(null, null, WriteOutcome.SourceOwned)]
    [InlineData(99, Visibility.Shared, WriteOutcome.NotAuthor)]
    [InlineData(99, Visibility.Private, WriteOutcome.NotFound)]
    public async Task UpdateAsync_RefusesAnEntryTheCallerDidNotWrite(
        int? author,
        Visibility? visibility,
        WriteOutcome expected
    )
    {
        GivenRepositoryHolds(AnEntry(author, visibility));

        var result = await _sut.UpdateAsync(5, 1, AnUpdate(), 1u, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Value);
        await _repository
            .DidNotReceive()
            .UpdateAsync(Arg.Any<Visit>(), Arg.Any<uint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNotFound_WhenNoSuchEntryExists()
    {
        GivenRepositoryHolds(null);

        var result = await _sut.UpdateAsync(5, 1, AnUpdate(), 1u, CancellationToken.None);

        Assert.Equal(WriteOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsVersionConflict_WhenTheRowMovedOn()
    {
        GivenRepositoryHolds(AnEntry(Viewer, Visibility.Shared));
        _repository
            .UpdateAsync(Arg.Any<Visit>(), Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.UpdateAsync(5, 1, AnUpdate(), 41u, CancellationToken.None);

        Assert.Equal(WriteOutcome.VersionConflict, result.Outcome);
    }

    [Fact]
    public async Task UpdateAsync_AppliesTheChangeAndPassesTheCallersVersionThrough()
    {
        var entry = AnEntry(Viewer, Visibility.Shared);
        GivenRepositoryHolds(entry);
        _repository
            .UpdateAsync(Arg.Any<Visit>(), Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _repository
            .GetByIdAsync(5, 1, Viewer, Arg.Any<CancellationToken>())
            .Returns(WithCareRecipient(entry));

        var result = await _sut.UpdateAsync(
            5,
            1,
            new UpdateVisitRequest
            {
                ScheduledAt = new DateTimeOffset(2026, 9, 21, 11, 0, 0, TimeSpan.Zero),
                Title = "Flyttet legetime",
                Notes = "Ny tid",
                Visibility = Visibility.Private,
            },
            41u,
            CancellationToken.None
        );

        Assert.Equal(WriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("Flyttet legetime", entry.Title);
        Assert.Equal(Visibility.Private, entry.Visibility);
        Assert.Equal(Now, entry.UpdatedAt);
        // The version the caller sent (41), so a change made in between is caught.
        await _repository.Received(1).UpdateAsync(entry, 41u, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_RefusesAnEntryWrittenByAnotherNextOfKin()
    {
        GivenRepositoryHolds(AnEntry(99, Visibility.Shared));

        var outcome = await _sut.DeleteAsync(5, 1, 1u, CancellationToken.None);

        Assert.Equal(WriteOutcome.NotAuthor, outcome);
        await _repository
            .DidNotReceive()
            .DeleteAsync(Arg.Any<Visit>(), Arg.Any<uint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheCallersOwnEntry()
    {
        var entry = AnEntry(Viewer, Visibility.Private);
        GivenRepositoryHolds(entry);
        _repository
            .DeleteAsync(Arg.Any<Visit>(), Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var outcome = await _sut.DeleteAsync(5, 1, 41u, CancellationToken.None);

        Assert.Equal(WriteOutcome.Succeeded, outcome);
        await _repository.Received(1).DeleteAsync(entry, 41u, Arg.Any<CancellationToken>());
    }

    private void GivenRepositoryHolds(Visit? entry) =>
        _repository
            .GetForWriteAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(entry);

    private static UpdateVisitRequest AnUpdate() =>
        new()
        {
            ScheduledAt = new DateTimeOffset(2026, 9, 21, 11, 0, 0, TimeSpan.Zero),
            Title = "Tittel",
            Visibility = Visibility.Shared,
        };

    private static Visit AnEntry(int? author, Visibility? visibility) =>
        new()
        {
            Id = 5,
            CareRecipientId = 1,
            ScheduledAt = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            Status = VisitStatus.Planned,
            Origin = author is null ? Origin.Synthetic : Origin.Portal,
            CreatedByNextOfKinId = author,
            Visibility = visibility,
        };

    private static Visit WithCareRecipient(Visit visit)
    {
        visit.CareRecipient = new CareRecipient { Id = visit.CareRecipientId, Name = "Vigdis" };
        return visit;
    }
}
