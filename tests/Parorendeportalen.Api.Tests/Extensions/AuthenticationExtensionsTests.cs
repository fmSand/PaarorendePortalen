using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Parorendeportalen.Api.Extensions;

namespace Parorendeportalen.Api.Tests.Extensions;

public class AuthenticationExtensionsTests
{
    private static IAuthenticationSchemeProvider ProviderFor(string environment)
    {
        var host = Substitute.For<IWebHostEnvironment>();
        host.EnvironmentName.Returns(environment);

        var services = new ServiceCollection().AddLogging();
        services.AddKinshipAuthentication(new ConfigurationBuilder().Build(), host);

        return services.BuildServiceProvider().GetRequiredService<IAuthenticationSchemeProvider>();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task OutsideDemo_OnlyTheSessionCookieAndOpenIdConnectAreRegistered(
        string environment
    )
    {
        var schemes = ProviderFor(environment);

        Assert.Equal(
            [
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme,
            ],
            (await schemes.GetAllSchemesAsync()).Select(scheme => scheme.Name).Order()
        );
        Assert.Equal(
            CookieAuthenticationDefaults.AuthenticationScheme,
            (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name
        );
        Assert.Equal(
            OpenIdConnectDefaults.AuthenticationScheme,
            (await schemes.GetDefaultChallengeSchemeAsync())?.Name
        );
    }

    [Fact]
    public async Task UnderDemo_OpenIdConnectIsNotRegistered()
    {
        var schemes = ProviderFor("Demo");

        Assert.DoesNotContain(
            OpenIdConnectDefaults.AuthenticationScheme,
            (await schemes.GetAllSchemesAsync()).Select(scheme => scheme.Name)
        );
        Assert.Empty(await schemes.GetRequestHandlerSchemesAsync());
    }

    [Fact]
    public async Task UnderDemo_EveryRequestAndEveryChallengeGoesToTheDemoScheme()
    {
        var schemes = ProviderFor("Demo");

        Assert.Equal("Demo", (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);
        Assert.Equal("Demo", (await schemes.GetDefaultChallengeSchemeAsync())?.Name);
    }
}
