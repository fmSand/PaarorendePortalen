using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Parorendeportalen.Api.Controllers;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Controllers;

public class ConsentsControllerTests
{
    private const int NextOfKinId = 5;
    private const int GrantedCareRecipientId = 7;
    private const int UngrantedCareRecipientId = 8;

    private readonly IConsentService _consentService = Substitute.For<IConsentService>();
    private readonly ICurrentNextOfKinAccessor _currentNextOfKin =
        Substitute.For<ICurrentNextOfKinAccessor>();
    private readonly ConsentsController _sut;

    public ConsentsControllerTests()
    {
        _currentNextOfKin
            .GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new CurrentNextOfKin(NextOfKinId, [GrantedCareRecipientId]));
        _sut = new ConsentsController(_consentService, _currentNextOfKin);
    }

    private Task AssertConsentNotQueried() =>
        _consentService
            .DidNotReceive()
            .GetConsentedCategoriesAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );

    [Fact]
    public async Task Get_ReturnsBadRequest_WhenCareRecipientIdOmitted()
    {
        var result = await _sut.Get(careRecipientId: null, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
        await AssertConsentNotQueried();
    }

    // Same posture as the visit endpoints: an ungranted id looks non-existent.
    [Fact]
    public async Task Get_ReturnsNotFound_WhenCallerHoldsNoGrantForTheCareRecipient()
    {
        var result = await _sut.Get(UngrantedCareRecipientId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await AssertConsentNotQueried();
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenTheSessionResolvesToNobody()
    {
        _currentNextOfKin
            .GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns((CurrentNextOfKin?)null);

        var result = await _sut.Get(GrantedCareRecipientId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await AssertConsentNotQueried();
    }

    [Fact]
    public async Task Get_ReturnsTheCallersConsentedCategories_ForThatCareRecipient()
    {
        _consentService
            .GetConsentedCategoriesAsync(
                NextOfKinId,
                GrantedCareRecipientId,
                Arg.Any<CancellationToken>()
            )
            .Returns([DataCategory.Visits]);

        var result = await _sut.Get(GrantedCareRecipientId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var categories = Assert.IsAssignableFrom<IReadOnlyList<DataCategory>>(okResult.Value);
        Assert.Equal([DataCategory.Visits], categories);
    }

    // The ids passed down are what stops one pair's consent answering for another.
    [Fact]
    public async Task Get_AsksForTheCallersOwnConsent_OverTheRequestedCareRecipient()
    {
        await _sut.Get(GrantedCareRecipientId, CancellationToken.None);

        await _consentService
            .Received(1)
            .GetConsentedCategoriesAsync(
                NextOfKinId,
                GrantedCareRecipientId,
                Arg.Any<CancellationToken>()
            );
    }
}
