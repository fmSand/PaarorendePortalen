using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Parorendeportalen.Api.Controllers;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Controllers;

public class VisitsControllerTests
{
    private const int CallersCareRecipientId = 7;

    private readonly IVisitService _visitService = Substitute.For<IVisitService>();
    private readonly ICurrentNextOfKinAccessor _currentNextOfKin = Substitute.For<ICurrentNextOfKinAccessor>();
    private readonly VisitsController _sut;

    public VisitsControllerTests()
    {
        _currentNextOfKin.GetCareRecipientIdAsync(Arg.Any<CancellationToken>()).Returns(CallersCareRecipientId);
        _sut = new VisitsController(_visitService, _currentNextOfKin);
    }

    private static VisitResponse CreateVisitResponse(int id, int careRecipientId) =>
        new(id, careRecipientId, "Kari Nordmann",
            new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero),
            null, VisitStatus.Planned, null, null);

    private void GivenServiceEchoesPagingBack() =>
        _visitService.GetByCareRecipientIdAsync(
                Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new PagedResponse<VisitResponse>([], call.ArgAt<int>(3), call.ArgAt<int>(4), 0));

    private static PagedResponse<VisitResponse> UnwrapPage(ActionResult<PagedResponse<VisitResponse>> result)
    {
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<PagedResponse<VisitResponse>>(okResult.Value);
    }

    [Fact]
    public async Task Get_NoPagingArgumentsSupplied_UsesFirstPageOfTwenty()
    {
        GivenServiceEchoesPagingBack();

        var result = await _sut.Get(from: null, to: null, cancellationToken: CancellationToken.None);

        var page = UnwrapPage(result);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(20, page.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public async Task Get_PageNumberBelowOne_ClampsToOne(int pageNumber)
    {
        GivenServiceEchoesPagingBack();

        var result = await _sut.Get(from: null, to: null, pageNumber, pageSize: 20, CancellationToken.None);

        Assert.Equal(1, UnwrapPage(result).PageNumber);
        await _visitService.Received(1).GetByCareRecipientIdAsync(
            Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(),
            1, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1000)]
    public async Task Get_PageNumberOneOrAbove_PassesThroughUnchanged(int pageNumber)
    {
        GivenServiceEchoesPagingBack();

        var result = await _sut.Get(from: null, to: null, pageNumber, pageSize: 20, CancellationToken.None);

        Assert.Equal(pageNumber, UnwrapPage(result).PageNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Get_PageSizeBelowOne_ClampsToOne(int pageSize)
    {
        GivenServiceEchoesPagingBack();

        var result = await _sut.Get(from: null, to: null, pageNumber: 1, pageSize, CancellationToken.None);

        Assert.Equal(1, UnwrapPage(result).PageSize);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public async Task Get_PageSizeAboveMaximum_ClampsToOneHundred(int pageSize)
    {
        GivenServiceEchoesPagingBack();

        var result = await _sut.Get(from: null, to: null, pageNumber: 1, pageSize, CancellationToken.None);

        Assert.Equal(100, UnwrapPage(result).PageSize);
        await _visitService.Received(1).GetByCareRecipientIdAsync(
            Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<int>(), 100, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100)]
    public async Task Get_PageSizeWithinBounds_PassesThroughUnchanged(int pageSize)
    {
        GivenServiceEchoesPagingBack();

        var result = await _sut.Get(from: null, to: null, pageNumber: 1, pageSize, CancellationToken.None);

        Assert.Equal(pageSize, UnwrapPage(result).PageSize);
    }

    [Fact]
    public async Task Get_ScopesQueryToCallersOwnCareRecipientId()
    {
        GivenServiceEchoesPagingBack();

        await _sut.Get(from: null, to: null, pageNumber: 1, pageSize: 20, CancellationToken.None);

        await _visitService.Received(1).GetByCareRecipientIdAsync(
            CallersCareRecipientId, Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_PassesDateFiltersToServiceUnchanged()
    {
        GivenServiceEchoesPagingBack();
        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero);

        await _sut.Get(from, to, pageNumber: 1, pageSize: 20, CancellationToken.None);

        await _visitService.Received(1).GetByCareRecipientIdAsync(
            Arg.Any<int>(), from, to, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ReturnsServicePayload_WhenVisitsExist()
    {
        var visits = new List<VisitResponse> { CreateVisitResponse(42, CallersCareRecipientId) };
        _visitService.GetByCareRecipientIdAsync(
                CallersCareRecipientId, Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResponse<VisitResponse>(visits, 1, 20, 1));

        var result = await _sut.Get(from: null, to: null, pageNumber: 1, pageSize: 20, CancellationToken.None);

        var page = UnwrapPage(result);
        Assert.Equal(1, page.TotalCount);
        var visit = Assert.Single(page.Items);
        Assert.Equal(42, visit.Id);
    }

    [Fact]
    public async Task GetById_ReturnsOkWithVisit_WhenFound()
    {
        _visitService.GetByIdAsync(42, CallersCareRecipientId, Arg.Any<CancellationToken>())
            .Returns(CreateVisitResponse(42, CallersCareRecipientId));

        var result = await _sut.GetById(42, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var visit = Assert.IsType<VisitResponse>(okResult.Value);
        Assert.Equal(42, visit.Id);
        Assert.Equal(CallersCareRecipientId, visit.CareRecipientId);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenServiceReturnsNull()
    {
        _visitService.GetByIdAsync(999, CallersCareRecipientId, Arg.Any<CancellationToken>())
            .Returns((VisitResponse?)null);

        var result = await _sut.GetById(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_PassesRequestedIdAndCallersCareRecipientId_AndDoesNotFallBackToAnotherVisit()
    {
        _visitService.GetByIdAsync(42, CallersCareRecipientId, Arg.Any<CancellationToken>())
            .Returns((VisitResponse?)null);
        _visitService.GetByIdAsync(
                Arg.Is<int>(id => id != 42), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CreateVisitResponse(99, CallersCareRecipientId));
        _visitService.GetByIdAsync(
                Arg.Any<int>(), Arg.Is<int>(id => id != CallersCareRecipientId), Arg.Any<CancellationToken>())
            .Returns(CreateVisitResponse(99, 3));

        var result = await _sut.GetById(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await _visitService.Received(1).GetByIdAsync(42, CallersCareRecipientId, Arg.Any<CancellationToken>());
    }
}
