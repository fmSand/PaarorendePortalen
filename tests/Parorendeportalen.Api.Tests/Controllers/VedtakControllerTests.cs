using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Parorendeportalen.Api.Controllers;
using Parorendeportalen.Api.Dtos.Planning;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Models.Planning;
using Parorendeportalen.Api.Services.Access;
using Parorendeportalen.Api.Services.Planning;

namespace Parorendeportalen.Api.Tests.Controllers;

public class VedtakControllerTests
{
    private const int GrantedCareRecipientId = 7;
    private const int UngrantedCareRecipientId = 8;
    private const int UnconsentedCareRecipientId = 9;

    private readonly IVedtakService _vedtakService = Substitute.For<IVedtakService>();
    private readonly IHealthDataAccessPolicy _accessPolicy =
        Substitute.For<IHealthDataAccessPolicy>();
    private readonly VedtakController _sut;

    public VedtakControllerTests()
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
        _sut = new VedtakController(_vedtakService, _accessPolicy);
    }

    [Fact]
    public async Task Get_AsksForTheVedtakCategoryAndNotTheVisitOne()
    {
        // Reusing Visits here would let consent to the visit log also open the municipality's decisions.
        _vedtakService
            .GetByCareRecipientIdAsync(GrantedCareRecipientId, Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.Get(GrantedCareRecipientId, CancellationToken.None);

        await _accessPolicy
            .Received(1)
            .AuthorizeReadAsync(
                GrantedCareRecipientId,
                DataCategory.Vedtak,
                Arg.Any<CancellationToken>()
            );
        await _accessPolicy
            .DidNotReceive()
            .AuthorizeReadAsync(Arg.Any<int>(), DataCategory.Visits, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ReturnsTheServicesList_WhenConsentIsGranted()
    {
        _vedtakService
            .GetByCareRecipientIdAsync(GrantedCareRecipientId, Arg.Any<CancellationToken>())
            .Returns([AVedtakResponse(1, "Hjemmesykepleie x2/dag")]);

        var result = await _sut.Get(GrantedCareRecipientId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var vedtak = Assert.IsAssignableFrom<IReadOnlyList<VedtakResponse>>(okResult.Value);
        Assert.Equal("Hjemmesykepleie x2/dag", Assert.Single(vedtak).Title);
    }

    [Fact]
    public async Task Get_ReturnsBadRequest_WithoutConsultingThePolicy_WhenCareRecipientIdOmitted()
    {
        var result = await _sut.Get(careRecipientId: null, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
        await _accessPolicy
            .DidNotReceive()
            .AuthorizeReadAsync(
                Arg.Any<int>(),
                Arg.Any<DataCategory>(),
                Arg.Any<CancellationToken>()
            );
        await AssertVedtakNotQueried();
    }

    // An ungranted id gets 404, the same as an id that doesn't exist (BOLA).
    [Fact]
    public async Task Get_ReturnsNotFound_WhenCallerHoldsNoGrant()
    {
        var result = await _sut.Get(UngrantedCareRecipientId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await AssertVedtakNotQueried();
    }

    [Fact]
    public async Task Get_ReturnsForbiddenProblem_WhenGrantIsHeldButConsentIsNot()
    {
        var result = await _sut.Get(UnconsentedCareRecipientId, CancellationToken.None);

        AssertForbiddenProblem(result.Result);
        await AssertVedtakNotQueried();
    }

    [Fact]
    public async Task GetById_ReturnsTheVedtak_WhenConsentIsGranted()
    {
        _vedtakService
            .GetByIdAsync(5, GrantedCareRecipientId, Arg.Any<CancellationToken>())
            .Returns(AVedtakResponse(5, "Fysioterapi hver onsdag"));

        var result = await _sut.GetById(5, GrantedCareRecipientId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(5, Assert.IsType<VedtakResponse>(okResult.Value).Id);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenTheServiceFindsNothing()
    {
        _vedtakService
            .GetByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((VedtakResponse?)null);

        var result = await _sut.GetById(999, GrantedCareRecipientId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_PassesTheCareRecipientScopeToTheService()
    {
        _vedtakService
            .GetByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((VedtakResponse?)null);

        await _sut.GetById(5, GrantedCareRecipientId, CancellationToken.None);

        // Without the scope the repository's BOLA filter never sees a value to filter on.
        await _vedtakService
            .Received(1)
            .GetByIdAsync(5, GrantedCareRecipientId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_ReturnsBadRequest_WhenCareRecipientIdOmitted()
    {
        var result = await _sut.GetById(5, careRecipientId: null, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
        await _vedtakService
            .DidNotReceive()
            .GetByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_ReturnsForbiddenProblem_WhenConsentIsNotHeld()
    {
        var result = await _sut.GetById(5, UnconsentedCareRecipientId, CancellationToken.None);

        AssertForbiddenProblem(result.Result);
        await _vedtakService
            .DidNotReceive()
            .GetByIdAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private Task AssertVedtakNotQueried() =>
        _vedtakService
            .DidNotReceive()
            .GetByCareRecipientIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());

    private static void AssertForbiddenProblem(IActionResult? result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    }

    private static VedtakResponse AVedtakResponse(int id, string title) =>
        new(
            id,
            GrantedCareRecipientId,
            ServiceType.Hjemmesykepleie,
            title,
            new RecurrenceResponse([DayOfWeek.Monday], 2),
            new DateOnly(2026, 1, 15),
            null,
            VedtakStatus.Active,
            []
        );
}
