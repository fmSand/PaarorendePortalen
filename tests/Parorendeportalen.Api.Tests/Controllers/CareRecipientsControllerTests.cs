using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Parorendeportalen.Api.Controllers;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Controllers;

public class CareRecipientsControllerTests
{
    private readonly ICareRecipientService _careRecipientService = Substitute.For<ICareRecipientService>();
    private readonly ICurrentNextOfKinAccessor _currentNextOfKin = Substitute.For<ICurrentNextOfKinAccessor>();
    private readonly CareRecipientsController _sut;

    public CareRecipientsControllerTests()
    {
        _sut = new CareRecipientsController(_careRecipientService, _currentNextOfKin);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_NotForbidden_WhenCallerHoldsNoGrantForThatCareRecipient()
    {
        _currentNextOfKin.HasAccessToAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.GetById(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await _careRecipientService.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_ReturnsOkWithCareRecipient_WhenCallerHoldsAGrant()
    {
        _currentNextOfKin.HasAccessToAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _careRecipientService.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new CareRecipientResponse(1, "Vigdis Quist"));

        var result = await _sut.GetById(1, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<CareRecipientResponse>(okResult.Value);
        Assert.Equal(1, response.Id);
        Assert.Equal("Vigdis Quist", response.Name);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenGrantedButServiceReturnsNull()
    {
        _currentNextOfKin.HasAccessToAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _careRecipientService.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns((CareRecipientResponse?)null);

        var result = await _sut.GetById(1, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await _careRecipientService.Received(1).GetByIdAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ReturnsEmptyList_WhenCallerHoldsNoGrant()
    {
        _currentNextOfKin.GetCareRecipientIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _careRecipientService.GetByIdsAsync(
                Arg.Is<IReadOnlyCollection<int>>(ids => ids.Count == 0), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.Get(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IEnumerable<CareRecipientResponse>>(okResult.Value);
        Assert.Empty(payload);
    }

    // The Fabian case (prosjektrapport 5.1): one next-of-kin, two recipients
    [Fact]
    public async Task Get_ReturnsEveryCareRecipientTheCallerHoldsAGrantFor()
    {
        _currentNextOfKin.GetCareRecipientIdsAsync(Arg.Any<CancellationToken>()).Returns([1, 2]);
        _careRecipientService.GetByIdsAsync(
                Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 1, 2 })),
                Arg.Any<CancellationToken>())
            .Returns([new CareRecipientResponse(2, "Tor Quist"), new CareRecipientResponse(1, "Vigdis Quist")]);

        var result = await _sut.Get(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IEnumerable<CareRecipientResponse>>(okResult.Value);
        Assert.Equal(["Tor Quist", "Vigdis Quist"], payload.Select(c => c.Name));
    }

    [Fact]
    public async Task Get_AsksOnlyForTheCareRecipientsTheCallerIsGranted()
    {
        _currentNextOfKin.GetCareRecipientIdsAsync(Arg.Any<CancellationToken>()).Returns([7]);
        _careRecipientService.GetByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.Get(CancellationToken.None);

        await _careRecipientService.Received(1).GetByIdsAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 7 })),
            Arg.Any<CancellationToken>());
    }
}
