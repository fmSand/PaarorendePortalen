using NSubstitute;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Services;

public class VisitServiceTests
{
    private readonly IVisitRepository _repository = Substitute.For<IVisitRepository>();
    private readonly VisitService _sut;

    public VisitServiceTests()
    {
        _sut = new VisitService(_repository);
    }

    private static Visit CreateVisit(
        int id,
        int careRecipientId,
        string careRecipientName,
        DateTimeOffset scheduledAt,
        VisitStatus status = VisitStatus.Planned,
        DateTimeOffset? actualAt = null,
        string? caregiverName = null,
        string? notes = null) => new()
        {
            Id = id,
            CareRecipientId = careRecipientId,
            CareRecipient = new CareRecipient { Id = careRecipientId, Name = careRecipientName },
            ScheduledAt = scheduledAt,
            ActualAt = actualAt,
            Status = status,
            CaregiverName = caregiverName,
            Notes = notes
        };

    private void SetupRepository(int careRecipientId, IReadOnlyList<Visit> items, int totalCount) =>
        _repository.GetByCareRecipientIdAsync(
                careRecipientId, Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
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
                notes: "Morgenstell og medisiner gitt.")
        };
        SetupRepository(1, visits, totalCount: 1);

        var result = await _sut.GetByCareRecipientIdAsync(1, from: null, to: null, pageNumber: 1, pageSize: 20, CancellationToken.None);

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

        var result = await _sut.GetByCareRecipientIdAsync(7, from: null, to: null, pageNumber: 1, pageSize: 20, CancellationToken.None);

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

        await _repository.Received(1).GetByCareRecipientIdAsync(99, from, to, 2, 10, cts.Token);
        await _repository.DidNotReceive().GetByCareRecipientIdAsync(
            Arg.Is<int>(id => id != 99), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_ReturnsVisitsInRepositoryOrder()
    {
        var earlier = CreateVisit(1, 1, "Kari Nordmann", new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero));
        var later = CreateVisit(2, 1, "Kari Nordmann", new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero));
        SetupRepository(1, new List<Visit> { earlier, later }, totalCount: 2);

        var result = await _sut.GetByCareRecipientIdAsync(1, from: null, to: null, pageNumber: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal([1, 2], result.Items.Select(v => v.Id));
    }

    [Fact]
    public async Task GetByCareRecipientIdAsync_MapsPagingMetadata_FromRequestAndRepositoryTotalCount()
    {
        SetupRepository(1, new List<Visit>(), totalCount: 47);

        var result = await _sut.GetByCareRecipientIdAsync(1, from: null, to: null, pageNumber: 3, pageSize: 20, CancellationToken.None);

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
            notes: "Morgenstell og medisiner gitt.");
        _repository.GetByIdAsync(42, 1, Arg.Any<CancellationToken>()).Returns(visit);

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
        _repository.GetByIdAsync(999, 1, Arg.Any<CancellationToken>()).Returns((Visit?)null);

        var response = await _sut.GetByIdAsync(999, 1, CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task GetByIdAsync_PassesIdAndCareRecipientIdScope_ToRepository()
    {
        using var cts = new CancellationTokenSource();
        _repository.GetByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((Visit?)null);

        await _sut.GetByIdAsync(id: 5, careRecipientId: 3, cts.Token);

        await _repository.Received(1).GetByIdAsync(5, 3, cts.Token);
        await _repository.DidNotReceive().GetByIdAsync(
            Arg.Any<int>(), Arg.Is<int>(careRecipientId => careRecipientId != 3), Arg.Any<CancellationToken>());
    }
}
