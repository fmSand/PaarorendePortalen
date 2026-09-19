using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NSubstitute;
using Parorendeportalen.Api.Extensions;
using Parorendeportalen.Api.Services.Kinship;

namespace Parorendeportalen.Api.Tests.Pipeline;

// The app's own authentication and authorization registrations over stand-in endpoints.
// Program.cs boots under Demo in tests, where every request authenticates and the fallback
// policy never rejects anything, so the Development challenge needs a host of its own.
public class ChallengePipelineTests
{
    private const string AuthorizationEndpoint = "https://idura.test/authorize";

    private static Task<IHost> StartDevelopmentHostAsync() =>
        new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .UseEnvironment("Development")
                    .ConfigureAppConfiguration(configuration =>
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["Idura:ClientId"] = "challenge-test-client",
                                ["Idura:ClientSecret"] = "challenge-test-secret",
                                ["Idura:Domain"] = "idura.test",
                            }
                        )
                    )
                    .ConfigureServices(
                        (context, services) =>
                        {
                            services.AddRouting();
                            services.AddKinshipAuthentication(
                                context.Configuration,
                                context.HostingEnvironment
                            );
                            services.AddKinshipAuthorization();

                            services.AddScoped(_ => Substitute.For<INextOfKinService>());

                            services.Configure<OpenIdConnectOptions>(
                                OpenIdConnectDefaults.AuthenticationScheme,
                                options =>
                                    options.Configuration = new OpenIdConnectConfiguration
                                    {
                                        Issuer = "https://idura.test/",
                                        AuthorizationEndpoint = AuthorizationEndpoint,
                                    }
                            );
                        }
                    )
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/api/fallback", () => "fallback policy");
                            endpoints.MapGet("/api/visits", () => "visits").RequireAuthorization();
                            endpoints.MapGet("/dashboard", () => "dashboard");
                            endpoints
                                .MapGet(
                                    "/api/auth/login",
                                    () =>
                                        Results.Challenge(
                                            new AuthenticationProperties { RedirectUri = "/" }
                                        )
                                )
                                .AllowAnonymous();
                        });
                    })
            )
            .StartAsync();

    // /api/visits carries the [Authorize] every controller has, so it runs the default policy.
    // /api/fallback carries nothing and runs the fallback policy.
    [Theory]
    [InlineData("/api/visits")]
    [InlineData("/api/fallback")]
    public async Task ApiRequestWithoutASession_Answers401(string path)
    {
        using var host = await StartDevelopmentHostAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task LoginWithoutASession_RedirectsToTheBrokerAuthorizationEndpoint()
    {
        using var host = await StartDevelopmentHostAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/api/auth/login");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(
            AuthorizationEndpoint,
            response.Headers.Location?.OriginalString,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task BrowserRequestOutsideApiWithoutASession_RedirectsToTheBrokerAuthorizationEndpoint()
    {
        using var host = await StartDevelopmentHostAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(
            AuthorizationEndpoint,
            response.Headers.Location?.OriginalString,
            StringComparison.Ordinal
        );
    }
}
