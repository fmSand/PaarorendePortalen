using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Parorendeportalen.Api.Controllers;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Controllers;

public class DayPlanControllerTests
{
    private const int CareRecipientId = 7;

    private static readonly DateOnly Monday = new(2026, 9, 7);

    // Already the next day in Norway, so a controller defaulting to UtcNow.Date opens on yesterday.
    private static readonly DateTimeOffset LateEveningUtc = new(
        2026,
        7,
        1,
        23,
        30,
        0,
        TimeSpan.Zero
    );

    private readonly IDayPlanService _dayPlanService = Substitute.For<IDayPlanService>();
    private readonly IHealthDataAccessPolicy _accessPolicy =
        Substitute.For<IHealthDataAccessPolicy>();
    private readonly FixedTimeProvider _clock = new(LateEveningUtc);
    private readonly DayPlanController _sut;

    public DayPlanControllerTests()
    {
        GivenConsentFor(DataCategory.Vedtak, AccessDecision.Granted);
        GivenConsentFor(DataCategory.Visits, AccessDecision.Granted);
        _dayPlanService
            .GetAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => AnEmptyPlan(call.ArgAt<DateOnly>(1)));
        _sut = new DayPlanController(_dayPlanService, _accessPolicy, _clock);
    }

    [Fact]
    public async Task Get_AsksForBothCategoriesThePlanIsBuiltFrom()
    {
        await _sut.Get(CareRecipientId, Monday, CancellationToken.None);

        await _accessPolicy
            .Received(1)
            .AuthorizeReadAsync(CareRecipientId, DataCategory.Vedtak, Arg.Any<CancellationToken>());
        await _accessPolicy
            .Received(1)
            .AuthorizeReadAsync(CareRecipientId, DataCategory.Visits, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ConsentForVisitsButNotVedtak_IsForbidden()
    {
        // The visit log alone is already served; letting it through here would answer a vedtak question with visit consent.
        GivenConsentFor(DataCategory.Vedtak, AccessDecision.DeniedNoConsent);

        var result = await _sut.Get(CareRecipientId, Monday, CancellationToken.None);

        AssertForbiddenProblem(result.Result);
        await AssertPlanNotBuilt();
    }

    [Fact]
    public async Task Get_ConsentForVedtakButNotVisits_IsForbidden()
    {
        GivenConsentFor(DataCategory.Visits, AccessDecision.DeniedNoConsent);

        var result = await _sut.Get(CareRecipientId, Monday, CancellationToken.None);

        AssertForbiddenProblem(result.Result);
        await AssertPlanNotBuilt();
    }

    [Fact]
    public async Task Get_ReturnsThePlan_WhenBothCategoriesAreGranted()
    {
        var result = await _sut.Get(CareRecipientId, Monday, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var plan = Assert.IsType<DayPlanResponse>(okResult.Value);
        Assert.Equal(Monday, plan.Date);
        Assert.Equal(CareRecipientId, plan.CareRecipientId);
    }

    [Fact]
    public async Task Get_NoDateGiven_UsesTodayInNorwayNotInUtc()
    {
        await _sut.Get(CareRecipientId, date: null, CancellationToken.None);

        // 23:30Z on 1 July is 01:30 on 2 July in Oslo.
        await _dayPlanService
            .Received(1)
            .GetAsync(CareRecipientId, new DateOnly(2026, 7, 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_DateGiven_UsesItRatherThanToday()
    {
        await _sut.Get(CareRecipientId, Monday, CancellationToken.None);

        await _dayPlanService
            .Received(1)
            .GetAsync(CareRecipientId, Monday, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ReturnsBadRequest_WithoutConsultingThePolicy_WhenCareRecipientIdOmitted()
    {
        var result = await _sut.Get(careRecipientId: null, Monday, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
        await _accessPolicy
            .DidNotReceive()
            .AuthorizeReadAsync(
                Arg.Any<int>(),
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            );
        await AssertPlanNotBuilt();
    }

    // 404 not 403, so an ungranted id looks the same as a non-existent one (BOLA)
    [Fact]
    public async Task Get_ReturnsNotFound_WhenCallerHoldsNoGrant()
    {
        GivenConsentFor(DataCategory.Vedtak, AccessDecision.DeniedNoKinship);
        GivenConsentFor(DataCategory.Visits, AccessDecision.DeniedNoKinship);

        var result = await _sut.Get(CareRecipientId, Monday, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await AssertPlanNotBuilt();
    }

    [Fact]
    public async Task Get_VedtakDenied_DoesNotGoOnToAskForVisits()
    {
        // Short-circuited, so one refusal writes one access-log row and not two.
        GivenConsentFor(DataCategory.Vedtak, AccessDecision.DeniedNoConsent);

        await _sut.Get(CareRecipientId, Monday, CancellationToken.None);

        await _accessPolicy
            .DidNotReceive()
            .AuthorizeReadAsync(Arg.Any<int>(), DataCategory.Visits, Arg.Any<CancellationToken>());
    }

    private void GivenConsentFor(DataCategory category, AccessDecision decision) =>
        _accessPolicy
            .AuthorizeReadAsync(CareRecipientId, category, Arg.Any<CancellationToken>())
            .Returns(decision);

    private Task AssertPlanNotBuilt() =>
        _dayPlanService
            .DidNotReceive()
            .GetAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());

    private static void AssertForbiddenProblem(IActionResult? result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    }

    private static DayPlanResponse AnEmptyPlan(DateOnly date) =>
        new(CareRecipientId, date, 0, 0, []);
}
