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
    public async Task GetById_ReturnsNotFound_NotForbidden_WhenRequestedIdIsNotCallersOwnCareRecipientId()
    {
        _currentNextOfKin.GetCareRecipientIdAsync(Arg.Any<CancellationToken>()).Returns(5);

        var result = await _sut.GetById(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await _careRecipientService.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_ReturnsOkWithCareRecipient_WhenRequestedIdIsCallersOwn()
    {
        _currentNextOfKin.GetCareRecipientIdAsync(Arg.Any<CancellationToken>()).Returns(1);
        _careRecipientService.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new CareRecipientResponse(1, "Kari Nordmann"));

        var result = await _sut.GetById(1, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<CareRecipientResponse>(okResult.Value);
        Assert.Equal(1, response.Id);
        Assert.Equal("Kari Nordmann", response.Name);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenOwnIdRequestedButServiceReturnsNull()
    {
        _currentNextOfKin.GetCareRecipientIdAsync(Arg.Any<CancellationToken>()).Returns(1);
        _careRecipientService.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns((CareRecipientResponse?)null);

        var result = await _sut.GetById(1, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await _careRecipientService.Received(1).GetByIdAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ReturnsEmptyList_WhenServiceReturnsNullForOwnCareRecipient()
    {
        _currentNextOfKin.GetCareRecipientIdAsync(Arg.Any<CancellationToken>()).Returns(1);
        _careRecipientService.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns((CareRecipientResponse?)null);

        var result = await _sut.Get(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IEnumerable<CareRecipientResponse>>(okResult.Value);
        Assert.Empty(payload);
    }

    [Fact]
    public async Task Get_ReturnsSingleItemList_WrappingOwnCareRecipient_WhenFound()
    {
        _currentNextOfKin.GetCareRecipientIdAsync(Arg.Any<CancellationToken>()).Returns(1);
        _careRecipientService.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new CareRecipientResponse(1, "Kari Nordmann"));

        var result = await _sut.Get(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IEnumerable<CareRecipientResponse>>(okResult.Value);
        var response = Assert.Single(payload);
        Assert.Equal(1, response.Id);
        Assert.Equal("Kari Nordmann", response.Name);
    }
}
