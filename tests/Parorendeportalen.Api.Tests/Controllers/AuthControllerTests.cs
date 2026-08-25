using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Parorendeportalen.Api.Controllers;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Tests.Controllers;

public class AuthControllerTests
{
    private readonly INextOfKinService _nextOfKinService = Substitute.For<INextOfKinService>();
    private readonly IAuthenticationService _authenticationService = Substitute.For<IAuthenticationService>();
    private readonly DefaultHttpContext _httpContext;
    private readonly AuthController _sut;

    public AuthControllerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_authenticationService);

        _httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var actionContext = new ActionContext(_httpContext, new RouteData(), new ControllerActionDescriptor());

        _sut = new AuthController(_nextOfKinService, Substitute.For<ILogger<AuthController>>())
        {
            ControllerContext = new ControllerContext(actionContext),
            Url = new UrlHelper(actionContext)
        };
    }

    private void GivenCallerHasClaims(params Claim[] claims) =>
        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme"));

    [Theory]
    [InlineData("/")]
    [InlineData("/visits")]
    [InlineData("/visits?page=2")]
    [InlineData("~/visits")]
    public void Login_LocalReturnUrl_ChallengesWithReturnUrlUnchanged(string returnUrl)
    {
        var result = Assert.IsType<ChallengeResult>(_sut.Login(returnUrl));

        Assert.Equal(returnUrl, result.Properties?.RedirectUri);
    }

    [Theory]
    [InlineData("https://evil.example.com/steal")]
    [InlineData("http://evil.example.com")]
    [InlineData("//evil.example.com")]
    [InlineData("/\\evil.example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void Login_NonLocalReturnUrl_ChallengesWithRootInstead(string returnUrl)
    {
        var result = Assert.IsType<ChallengeResult>(_sut.Login(returnUrl));

        Assert.Equal("/", result.Properties?.RedirectUri);
        Assert.NotEqual(returnUrl, result.Properties?.RedirectUri);
    }

    [Fact]
    public void Login_NullReturnUrl_ChallengesWithRoot()
    {
        var result = Assert.IsType<ChallengeResult>(_sut.Login(null));

        Assert.Equal("/", result.Properties?.RedirectUri);
    }

    [Fact]
    public void Login_AnyReturnUrl_ChallengesOpenIdConnectScheme()
    {
        var result = Assert.IsType<ChallengeResult>(_sut.Login("/visits"));

        var scheme = Assert.Single(result.AuthenticationSchemes);
        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, scheme);
    }

    [Fact]
    public async Task Me_ReturnsUnauthorized_WhenSubClaimMissing()
    {
        GivenCallerHasClaims(new Claim("name", "Frida Sand"));

        var result = await _sut.Me(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        await _nextOfKinService.DidNotReceive()
            .GetByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Me_ReturnsOkWithNextOfKin_WhenSubClaimPresentAndNextOfKinFound()
    {
        GivenCallerHasClaims(new Claim("sub", "sub-123"));
        _nextOfKinService.GetByExternalIdAsync("sub-123", Arg.Any<CancellationToken>())
            .Returns(new NextOfKinResponse(1, "Frida Sand", 7));

        var result = await _sut.Me(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<NextOfKinResponse>(okResult.Value);
        Assert.Equal(1, response.Id);
        Assert.Equal("Frida Sand", response.DisplayName);
        Assert.Equal(7, response.CareRecipientId);
    }

    [Fact]
    public async Task Me_ReturnsNotFound_WhenSubClaimPresentButNextOfKinNotFound()
    {
        GivenCallerHasClaims(new Claim("sub", "unknown-sub"));
        _nextOfKinService.GetByExternalIdAsync("unknown-sub", Arg.Any<CancellationToken>())
            .Returns((NextOfKinResponse?)null);

        var result = await _sut.Me(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Me_LooksUpCallersOwnSubClaim_AndDoesNotFallBackToAnotherNextOfKin()
    {
        GivenCallerHasClaims(new Claim("sub", "sub-123"));
        _nextOfKinService.GetByExternalIdAsync("sub-123", Arg.Any<CancellationToken>())
            .Returns((NextOfKinResponse?)null);
        _nextOfKinService.GetByExternalIdAsync(
            Arg.Is<string>(externalId => externalId != "sub-123"), Arg.Any<CancellationToken>())
            .Returns(new NextOfKinResponse(2, "Someone Else", 9));

        var result = await _sut.Me(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        await _nextOfKinService.Received(1)
            .GetByExternalIdAsync("sub-123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logout_SignsOutCookieScheme_WhenSubClaimPresentAndNextOfKinFound()
    {
        GivenCallerHasClaims(new Claim("sub", "sub-123"));
        _nextOfKinService.GetByExternalIdAsync("sub-123", Arg.Any<CancellationToken>())
            .Returns(new NextOfKinResponse(1, "Frida Sand", 7));

        var result = await _sut.Logout(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        await _authenticationService.Received(1).SignOutAsync(
            _httpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Any<AuthenticationProperties?>());
    }

    [Fact]
    public async Task Logout_StillSignsOut_WhenSubClaimPresentButNextOfKinNotFound()
    {
        GivenCallerHasClaims(new Claim("sub", "unknown-sub"));
        _nextOfKinService.GetByExternalIdAsync("unknown-sub", Arg.Any<CancellationToken>())
            .Returns((NextOfKinResponse?)null);

        var result = await _sut.Logout(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        await _authenticationService.Received(1).SignOutAsync(
            _httpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Any<AuthenticationProperties?>());
    }

    [Fact]
    public async Task Logout_StillSignsOut_WhenSubClaimMissing()
    {
        GivenCallerHasClaims(new Claim("name", "Frida Sand"));

        var result = await _sut.Logout(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        await _authenticationService.Received(1).SignOutAsync(
            _httpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Any<AuthenticationProperties?>());
    }

    [Fact]
    public async Task Logout_DoesNotQueryNextOfKin_WhenSubClaimMissing()
    {
        GivenCallerHasClaims(new Claim("name", "Frida Sand"));

        await _sut.Logout(CancellationToken.None);

        await _nextOfKinService.DidNotReceive()
            .GetByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
