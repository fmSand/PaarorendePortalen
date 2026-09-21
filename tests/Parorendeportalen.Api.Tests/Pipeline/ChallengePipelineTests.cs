using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Parorendeportalen.Api.Dtos.Kinship;
using Parorendeportalen.Api.Extensions;
using Parorendeportalen.Api.Services.Kinship;

namespace Parorendeportalen.Api.Tests.Pipeline;

// The app's own authentication and authorization registrations over stand-in endpoints.
// Program.cs boots under Demo in tests, where every request authenticates and the fallback
// policy never rejects anything, so the Development login flow needs a host of its own.
public class ChallengePipelineTests
{
    private const string Issuer = "https://idura.test/";
    private const string AuthorizationEndpoint = "https://idura.test/authorize";
    private const string ClientId = "challenge-test-client";
    private const string Sub = "idura-sub-1";
    private const string NationalId = "12345678901";

    private static readonly SymmetricSecurityKey SigningKey = new(
        RandomNumberGenerator.GetBytes(32)
    );

    private static Task<IHost> StartDevelopmentHostAsync(Func<string>? idToken = null) =>
        new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .UseEnvironment("Development")
                    .ConfigureAppConfiguration(configuration =>
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["Idura:ClientId"] = ClientId,
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

                            var nextOfKin = Substitute.For<INextOfKinService>();
                            nextOfKin
                                .ResolveOrBindAsync(
                                    Sub,
                                    NationalId,
                                    Arg.Any<string>(),
                                    Arg.Any<CancellationToken>()
                                )
                                .Returns(new NextOfKinResponse(1, Sub, []));
                            services.AddSingleton(nextOfKin);

                            services.Configure<OpenIdConnectOptions>(
                                OpenIdConnectDefaults.AuthenticationScheme,
                                options =>
                                {
                                    options.Configuration = new OpenIdConnectConfiguration
                                    {
                                        Issuer = Issuer,
                                        AuthorizationEndpoint = AuthorizationEndpoint,
                                        SigningKeys = { SigningKey },
                                    };

                                    if (idToken is not null)
                                    {
                                        // Stands in for the token endpoint call
                                        options.Events.OnAuthorizationCodeReceived = received =>
                                        {
                                            received.HandleCodeRedemption(
                                                "test-access-token",
                                                idToken()
                                            );
                                            return Task.CompletedTask;
                                        };
                                    }
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
                            endpoints.MapGet(
                                "/api/session/claims",
                                (ClaimsPrincipal user) => user.Claims.Select(claim => claim.Type)
                            );
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

    [Fact]
    public async Task CompletedLogin_KeepsSocialNoOutOfTheSession()
    {
        var idToken = "";
        using var host = await StartDevelopmentHostAsync(() => idToken);
        using var client = new HttpClient(
            new CookieContainerHandler { InnerHandler = host.GetTestServer().CreateHandler() }
        )
        {
            BaseAddress = new Uri("https://localhost"),
        };

        var challenge = await client.GetAsync("/api/auth/login");
        var authorizeQuery = QueryHelpers.ParseQuery(challenge.Headers.Location?.Query);
        idToken = IdToken(nonce: authorizeQuery["nonce"].ToString());

        var callback = await client.GetAsync(
            $"/callback?code=test-code&state={authorizeQuery["state"]}"
        );
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location?.OriginalString);

        var claimTypes = await client.GetFromJsonAsync<string[]>("/api/session/claims");

        Assert.NotNull(claimTypes);
        Assert.Contains("sub", claimTypes);
        Assert.DoesNotContain("socialno", claimTypes);
    }

    private static string IdToken(string nonce) =>
        new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = ClientId,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = Sub,
                    ["socialno"] = NationalId,
                    ["nonce"] = nonce,
                },
                SigningCredentials = new SigningCredentials(
                    SigningKey,
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );
}
