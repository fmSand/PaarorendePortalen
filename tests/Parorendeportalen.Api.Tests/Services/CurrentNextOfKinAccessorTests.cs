using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Services;

public class CurrentNextOfKinAccessorTests
{
    private readonly INextOfKinService _nextOfKinService = Substitute.For<INextOfKinService>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly CurrentNextOfKinAccessor _sut;

    public CurrentNextOfKinAccessorTests()
    {
        _sut = new CurrentNextOfKinAccessor(_httpContextAccessor, _nextOfKinService);
    }

    private static DefaultHttpContext CreateAuthenticatedHttpContext(string? subClaim)
    {
        var context = new DefaultHttpContext();
        var claims = subClaim is null
            ? Array.Empty<Claim>()
            : [new Claim("sub", subClaim)];
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"));
        return context;
    }

    [Theory]
    [InlineData("caller-A", 1)]
    [InlineData("caller-B", 2)]
    public async Task GetCareRecipientIdsAsync_ResolvesTheCallersOwnGrants(string subClaim, int careRecipientId)
    {
        _httpContextAccessor.HttpContext.Returns(CreateAuthenticatedHttpContext(subClaim));
        _nextOfKinService.GetCareRecipientIdsByExternalIdAsync(subClaim, Arg.Any<CancellationToken>())
            .Returns([careRecipientId]);

        var result = await _sut.GetCareRecipientIdsAsync(CancellationToken.None);

        Assert.Equal([careRecipientId], result);
        await _nextOfKinService.Received(1)
            .GetCareRecipientIdsByExternalIdAsync(subClaim, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCareRecipientIdsAsync_ReturnsEveryGrant_WhenCallerHoldsSeveral()
    {
        _httpContextAccessor.HttpContext.Returns(CreateAuthenticatedHttpContext("sibling"));
        _nextOfKinService.GetCareRecipientIdsByExternalIdAsync("sibling", Arg.Any<CancellationToken>())
            .Returns([1, 2]);

        var result = await _sut.GetCareRecipientIdsAsync(CancellationToken.None);

        Assert.Equal([1, 2], result);
    }

    // A caller with no current grant is a legitimate state, not a server error -
    // the controllers turn it into an empty list or a 404
    [Fact]
    public async Task GetCareRecipientIdsAsync_ReturnsEmpty_WhenCallerHoldsNoGrant()
    {
        _httpContextAccessor.HttpContext.Returns(CreateAuthenticatedHttpContext("unrecognized-sub"));
        _nextOfKinService.GetCareRecipientIdsByExternalIdAsync("unrecognized-sub", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.GetCareRecipientIdsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCareRecipientIdsAsync_Throws_WhenNoHttpContext()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetCareRecipientIdsAsync(CancellationToken.None));
        Assert.Contains("HttpContext", exception.Message);
    }

    [Fact]
    public async Task GetCareRecipientIdsAsync_Throws_WhenNoSubClaim()
    {
        _httpContextAccessor.HttpContext.Returns(CreateAuthenticatedHttpContext(subClaim: null));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetCareRecipientIdsAsync(CancellationToken.None));
        Assert.Contains("sub", exception.Message);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task HasAccessToAsync_IsTrueOnlyForACareRecipientTheCallerHoldsAGrantFor(
        int careRecipientId, bool expected)
    {
        _httpContextAccessor.HttpContext.Returns(CreateAuthenticatedHttpContext("sibling"));
        _nextOfKinService.GetCareRecipientIdsByExternalIdAsync("sibling", Arg.Any<CancellationToken>())
            .Returns([1, 2]);

        var result = await _sut.HasAccessToAsync(careRecipientId, CancellationToken.None);

        Assert.Equal(expected, result);
    }
}
