using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Parorendeportalen.Api.Controllers;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Controllers;

public class VisitsControllerTests
{
    private const int GrantedCareRecipientId = 7;
    private const int UngrantedCareRecipientId = 8;
    private const int UnconsentedCareRecipientId = 9;

    private readonly IVisitService _visitService = Substitute.For<IVisitService>();
    private readonly IHealthDataAccessPolicy _accessPolicy =
        Substitute.For<IHealthDataAccessPolicy>();
    private readonly VisitsController _sut;

    public VisitsControllerTests()
    {
        _accessPolicy
            .AuthorizeReadAsync(
                GrantedCareRecipientId,
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(AccessDecision.Granted);
        _accessPolicy
            .AuthorizeReadAsync(
                UngrantedCareRecipientId,
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(AccessDecision.DeniedNoKinship);
        _accessPolicy
            .AuthorizeReadAsync(
                UnconsentedCareRecipientId,
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(AccessDecision.DeniedNoConsent);
        _sut = new VisitsController(_visitService, _accessPolicy);
    }

    private static VisitResponse CreateVisitResponse(int id, int careRecipientId) =>
        new(
            id,
            careRecipientId,
            "Vigdis Quist",
            new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero),
            null,
            VisitStatus.Planned,
            null,
            null
        );

    private void GivenServiceEchoesPagingBack() =>
        _visitService
            .GetByCareRecipientIdAsync(
                Arg.Any<int>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new PagedResponse<VisitResponse>(
                [],
                call.ArgAt<int>(3),
                call.ArgAt<int>(4),
                0
            ));

    private Task<ActionResult<PagedResponse<VisitResponse>>> Get(
        int? careRecipientId = GrantedCareRecipientId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int pageNumber = 1,
        int pageSize = 20
    ) => _sut.Get(careRecipientId, from, to, pageNumber, pageSize, CancellationToken.None);

    private static PagedResponse<VisitResponse> UnwrapPage(
        ActionResult<PagedResponse<VisitResponse>> result
    )
    {
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<PagedResponse<VisitResponse>>(okResult.Value);
    }

    private static void AssertForbiddenProblem(IActionResult? result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    }

    private Task AssertVisitListNotQueried() =>
        _visitService
            .DidNotReceive()
            .GetByCareRecipientIdAsync(
                Arg.Any<int>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );

    [Fact]
    public async Task Get_NoPagingArgumentsSupplied_UsesFirstPageOfTwenty()
    {
        GivenServiceEchoesPagingBack();

        var result = await _sut.Get(
            GrantedCareRecipientId,
            from: null,
            to: null,
            cancellationToken: CancellationToken.None
        );

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

        var result = await Get(pageNumber: pageNumber);

        Assert.Equal(1, UnwrapPage(result).PageNumber);
        await _visitService
            .Received(1)
            .GetByCareRecipientIdAsync(
                Arg.Any<int>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                1,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1000)]
    public async Task Get_PageNumberOneOrAbove_PassesThroughUnchanged(int pageNumber)
    {
        GivenServiceEchoesPagingBack();

        var result = await Get(pageNumber: pageNumber);

        Assert.Equal(pageNumber, UnwrapPage(result).PageNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Get_PageSizeBelowOne_ClampsToOne(int pageSize)
    {
        GivenServiceEchoesPagingBack();

        var result = await Get(pageSize: pageSize);

        Assert.Equal(1, UnwrapPage(result).PageSize);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public async Task Get_PageSizeAboveMaximum_ClampsToOneHundred(int pageSize)
    {
        GivenServiceEchoesPagingBack();

        var result = await Get(pageSize: pageSize);

        Assert.Equal(100, UnwrapPage(result).PageSize);
        await _visitService
            .Received(1)
            .GetByCareRecipientIdAsync(
                Arg.Any<int>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                100,
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100)]
    public async Task Get_PageSizeWithinBounds_PassesThroughUnchanged(int pageSize)
    {
        GivenServiceEchoesPagingBack();

        var result = await Get(pageSize: pageSize);

        Assert.Equal(pageSize, UnwrapPage(result).PageSize);
    }

    [Fact]
    public async Task Get_ScopesQueryToTheRequestedCareRecipient()
    {
        GivenServiceEchoesPagingBack();

        await Get();

        await _visitService
            .Received(1)
            .GetByCareRecipientIdAsync(
                GrantedCareRecipientId,
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    // A consent for another category must not open the visit log.
    [Fact]
    public async Task Get_AuthorizesTheVisitsCategory_ForTheRequestedCareRecipient()
    {
        GivenServiceEchoesPagingBack();

        await Get();

        await _accessPolicy
            .Received(1)
            .AuthorizeReadAsync(
                GrantedCareRecipientId,
                DataCategory.Visits,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Get_ReturnsBadRequest_WithoutConsultingThePolicy_WhenCareRecipientIdOmitted()
    {
        var result = await Get(careRecipientId: null);

        Assert.IsType<ObjectResult>(result.Result);
        await _accessPolicy
            .DidNotReceive()
            .AuthorizeReadAsync(
                Arg.Any<int>(),
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            );
        await AssertVisitListNotQueried();
    }

    // 404 not 403, so an ungranted id looks the same as a non-existent one (BOLA)
    [Fact]
    public async Task Get_ReturnsNotFound_WhenCallerHoldsNoGrantForTheRequestedCareRecipient()
    {
        var result = await Get(careRecipientId: UngrantedCareRecipientId);

        Assert.IsType<NotFoundResult>(result.Result);
        await AssertVisitListNotQueried();
    }

    // The caller holds a grant, so a 403 tells them nothing they did not know.
    [Fact]
    public async Task Get_ReturnsForbiddenProblem_WhenCallerHoldsAGrantButNoConsent()
    {
        var result = await Get(careRecipientId: UnconsentedCareRecipientId);

        AssertForbiddenProblem(result.Result);
        await AssertVisitListNotQueried();
    }

    [Fact]
    public async Task Get_PassesDateFiltersToServiceUnchanged()
    {
        GivenServiceEchoesPagingBack();
        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero);

        await Get(from: from, to: to);

        await _visitService
            .Received(1)
            .GetByCareRecipientIdAsync(
                Arg.Any<int>(),
                from,
                to,
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Get_ReturnsServicePayload_WhenVisitsExist()
    {
        var visits = new List<VisitResponse> { CreateVisitResponse(42, GrantedCareRecipientId) };
        _visitService
            .GetByCareRecipientIdAsync(
                GrantedCareRecipientId,
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new PagedResponse<VisitResponse>(visits, 1, 20, 1));

        var result = await Get();

        var page = UnwrapPage(result);
        Assert.Equal(1, page.TotalCount);
        var visit = Assert.Single(page.Items);
        Assert.Equal(42, visit.Id);
    }

    [Fact]
    public async Task GetById_ReturnsOkWithVisit_WhenFound()
    {
        _visitService
            .GetByIdAsync(42, GrantedCareRecipientId, Arg.Any<CancellationToken>())
            .Returns(CreateVisitResponse(42, GrantedCareRecipientId));

        var result = await _sut.GetById(42, GrantedCareRecipientId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var visit = Assert.IsType<VisitResponse>(okResult.Value);
        Assert.Equal(42, visit.Id);
        Assert.Equal(GrantedCareRecipientId, visit.CareRecipientId);
    }

    [Fact]
    public async Task GetById_AuthorizesTheVisitsCategory_ForTheRequestedCareRecipient()
    {
        await _sut.GetById(42, GrantedCareRecipientId, CancellationToken.None);

        await _accessPolicy
            .Received(1)
            .AuthorizeReadAsync(
                GrantedCareRecipientId,
                DataCategory.Visits,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenServiceReturnsNull()
    {
        _visitService
            .GetByIdAsync(999, GrantedCareRecipientId, Arg.Any<CancellationToken>())
            .Returns((VisitResponse?)null);

        var result = await _sut.GetById(999, GrantedCareRecipientId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsBadRequest_WhenCareRecipientIdOmitted()
    {
        var result = await _sut.GetById(42, careRecipientId: null, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
        await _visitService
            .DidNotReceive()
            .GetByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WithoutQueryingService_WhenCallerHoldsNoGrant()
    {
        var result = await _sut.GetById(42, UngrantedCareRecipientId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await _visitService
            .DidNotReceive()
            .GetByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_ReturnsForbiddenProblem_WithoutQueryingService_WhenCallerHoldsNoConsent()
    {
        var result = await _sut.GetById(42, UnconsentedCareRecipientId, CancellationToken.None);

        AssertForbiddenProblem(result.Result);
        await _visitService
            .DidNotReceive()
            .GetByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_PassesRequestedIdAndCareRecipientId_AndDoesNotFallBackToAnotherVisit()
    {
        _visitService
            .GetByIdAsync(42, GrantedCareRecipientId, Arg.Any<CancellationToken>())
            .Returns((VisitResponse?)null);
        _visitService
            .GetByIdAsync(Arg.Is<int>(id => id != 42), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CreateVisitResponse(99, GrantedCareRecipientId));
        _visitService
            .GetByIdAsync(
                Arg.Any<int>(),
                Arg.Is<int>(id => id != GrantedCareRecipientId),
                Arg.Any<CancellationToken>()
            )
            .Returns(CreateVisitResponse(99, 3));

        var result = await _sut.GetById(42, GrantedCareRecipientId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await _visitService
            .Received(1)
            .GetByIdAsync(42, GrantedCareRecipientId, Arg.Any<CancellationToken>());
    }
}
