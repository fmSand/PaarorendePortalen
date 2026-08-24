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

    [Fact]
    public async Task GetCareRecipientIdAsync_ReturnsOwnCareRecipientId_ForAuthenticatedSubClaim()
    {
        _httpContextAccessor.HttpContext.Returns(CreateAuthenticatedHttpContext("caller-123"));
        _nextOfKinService.GetCareRecipientIdByExternalIdAsync("caller-123", Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await _sut.GetCareRecipientIdAsync(CancellationToken.None);

        Assert.Equal(1, result);
        await _nextOfKinService.Received(1).GetCareRecipientIdByExternalIdAsync("caller-123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCareRecipientIdAsync_ReturnsCallerAsId_WhenCallerAIsAuthenticated()
    {
        _httpContextAccessor.HttpContext.Returns(CreateAuthenticatedHttpContext("caller-A"));
        _nextOfKinService.GetCareRecipientIdByExternalIdAsync("caller-A", Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await _sut.GetCareRecipientIdAsync(CancellationToken.None);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task GetCareRecipientIdAsync_ReturnsCallerBsId_WhenCallerBIsAuthenticated()
    {
        _httpContextAccessor.HttpContext.Returns(CreateAuthenticatedHttpContext("caller-B"));
        _nextOfKinService.GetCareRecipientIdByExternalIdAsync("caller-B", Arg.Any<CancellationToken>())
            .Returns(2);

        var result = await _sut.GetCareRecipientIdAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task GetCareRecipientIdAsync_Throws_WhenNoHttpContext()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetCareRecipientIdAsync(CancellationToken.None));
        Assert.Contains("HttpContext", exception.Message);
    }

    [Fact]
    public async Task GetCareRecipientIdAsync_Throws_WhenNoSubClaim()
    {
        _httpContextAccessor.HttpContext.Returns(CreateAuthenticatedHttpContext(subClaim: null));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetCareRecipientIdAsync(CancellationToken.None));
        Assert.Contains("sub", exception.Message);
    }

    [Fact]
    public async Task GetCareRecipientIdAsync_Throws_WhenNoMatchingNextOfKinForSubClaim()
    {
        _httpContextAccessor.HttpContext.Returns(CreateAuthenticatedHttpContext("unrecognized-sub"));
        _nextOfKinService.GetCareRecipientIdByExternalIdAsync("unrecognized-sub", Arg.Any<CancellationToken>())
            .Returns((int?)null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetCareRecipientIdAsync(CancellationToken.None));
        Assert.Contains("unrecognized-sub", exception.Message);
    }
}
