using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Parorendeportalen.Api.Authentication;
using Parorendeportalen.Api.Dtos.Kinship;
using Parorendeportalen.Api.Services.Kinship;

namespace Parorendeportalen.Api.Tests.Authentication;

public class LoginValidatorTests
{
    private const string Sub = "idura-sub-1";
    private const string NationalId = "12345678901";

    private readonly INextOfKinService _nextOfKin = Substitute.For<INextOfKinService>();
    private readonly LoginValidator _sut;

    public LoginValidatorTests()
    {
        _sut = new LoginValidator(_nextOfKin, NullLogger<LoginValidator>.Instance);
    }

    private static ClaimsPrincipal Principal(string? sub, string? socialNo, string? name = null) =>
        new(
            new ClaimsIdentity(
                new (string Type, string? Value)[]
                {
                    ("sub", sub),
                    ("socialno", socialNo),
                    ("name", name),
                }
                    .Where(claim => claim.Value is not null)
                    .Select(claim => new Claim(claim.Type, claim.Value!)),
                "oidc"
            )
        );

    private void BindsAs(int nextOfKinId, string displayName) =>
        _nextOfKin
            .ResolveOrBindAsync(Sub, NationalId, displayName, Arg.Any<CancellationToken>())
            .Returns(new NextOfKinResponse(nextOfKinId, displayName, []));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WithoutASub_IsRejectedBeforeAnyBinding(string? sub)
    {
        var result = await _sut.ValidateAsync(Principal(sub, NationalId), CancellationToken.None);

        Assert.Equal(
            new LoginResult(FailureReason: LoginFailureReasons.NoSubClaim, NextOfKinId: null),
            result
        );
        await _nextOfKin
            .DidNotReceiveWithAnyArgs()
            .ResolveOrBindAsync(default!, default!, default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WithoutASocialNo_IsRejectedBeforeAnyBinding(string? socialNo)
    {
        var result = await _sut.ValidateAsync(Principal(Sub, socialNo), CancellationToken.None);

        Assert.Equal(
            new LoginResult(FailureReason: LoginFailureReasons.NoSocialNoClaim, NextOfKinId: null),
            result
        );
        await _nextOfKin
            .DidNotReceiveWithAnyArgs()
            .ResolveOrBindAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task IdentityWithoutAGrant_IsRejected()
    {
        _nextOfKin
            .ResolveOrBindAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((NextOfKinResponse?)null);

        var result = await _sut.ValidateAsync(
            Principal(Sub, NationalId, "Kari Nordmann"),
            CancellationToken.None
        );

        Assert.Equal(
            new LoginResult(FailureReason: LoginFailureReasons.NoGrant, NextOfKinId: null),
            result
        );
    }

    [Fact]
    public async Task IdentityWithAGrant_IsAcceptedAsThatNextOfKin()
    {
        BindsAs(nextOfKinId: 7, displayName: "Kari Nordmann");

        var result = await _sut.ValidateAsync(
            Principal(Sub, NationalId, "Kari Nordmann"),
            CancellationToken.None
        );

        Assert.Equal(new LoginResult(FailureReason: null, NextOfKinId: 7), result);
    }

    [Fact]
    public async Task WithoutAName_TheSubStandsInAsDisplayName()
    {
        BindsAs(nextOfKinId: 7, displayName: Sub);

        var result = await _sut.ValidateAsync(Principal(Sub, NationalId), CancellationToken.None);

        Assert.Equal(new LoginResult(FailureReason: null, NextOfKinId: 7), result);
    }
}
