using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
using Parorendeportalen.Api.Authentication;
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
    private const string SubWithoutAGrant = "idura-sub-2";
    private const string NationalId = "12345678901";
    private const string DisplayName = "Kari Nordmann";

    private static readonly SymmetricSecurityKey SigningKey = new(
        RandomNumberGenerator.GetBytes(32)
    );

    private string _idToken = "";

    private Task<IHost> StartDevelopmentHostAsync() =>
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
                                    DisplayName,
                                    Arg.Any<CancellationToken>()
                                )
                                .Returns(new NextOfKinResponse(1, Sub, []));
                            nextOfKin
                                .ResolveOrBindAsync(
                                    SubWithoutAGrant,
                                    NationalId,
                                    Arg.Any<string>(),
                                    Arg.Any<CancellationToken>()
                                )
                                .Returns((NextOfKinResponse?)null);
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

                                    // Stands in for the token endpoint call
                                    options.Events.OnAuthorizationCodeReceived = received =>
                                    {
                                        received.HandleCodeRedemption(
                                            "test-access-token",
                                            _idToken
                                        );
                                        return Task.CompletedTask;
                                    };
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
    public async Task CompletedLogin_KeepsOnlySubInTheSession()
    {
        using var host = await StartDevelopmentHostAsync();
        using var client = BrowserClient(host);

        var callback = await CallbackAsync(client, Sub);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location?.OriginalString);

        var claimTypes = await client.GetFromJsonAsync<string[]>("/api/session/claims");

        Assert.Equal(["sub"], claimTypes!);
    }

    [Fact]
    public async Task RefusedLogin_Answers403WithTheReason()
    {
        using var host = await StartDevelopmentHostAsync();
        using var client = BrowserClient(host);

        var callback = await CallbackAsync(client, SubWithoutAGrant);

        Assert.Equal(HttpStatusCode.Forbidden, callback.StatusCode);
        var problem = await callback.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(LoginFailureReasons.NoGrant, problem?.Detail);
    }

    [Fact]
    public async Task RefusedLogin_StartsNoSession()
    {
        using var host = await StartDevelopmentHostAsync();
        using var client = BrowserClient(host);

        await CallbackAsync(client, SubWithoutAGrant);
        var session = await client.GetAsync("/api/session/claims");

        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Fact]
    public async Task CallbackWithAStateWeNeverIssued_HidesTheHandlersReason()
    {
        using var host = await StartDevelopmentHostAsync();
        using var client = host.GetTestClient();

        var callback = await client.GetAsync("/callback?code=test-code&state=forged");

        Assert.Equal(HttpStatusCode.Forbidden, callback.StatusCode);
        var problem = await callback.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Login failed.", problem?.Detail);
    }

    // Keeps cookies across requests, as the browser does through the redirects
    private static HttpClient BrowserClient(IHost host) =>
        new(new CookieContainerHandler { InnerHandler = host.GetTestServer().CreateHandler() })
        {
            BaseAddress = new Uri("https://localhost"),
        };

    private async Task<HttpResponseMessage> CallbackAsync(HttpClient client, string sub)
    {
        var challenge = await client.GetAsync("/api/auth/login");
        var authorizeQuery = QueryHelpers.ParseQuery(challenge.Headers.Location?.Query);
        _idToken = IdToken(sub, nonce: authorizeQuery["nonce"].ToString());

        return await client.GetAsync($"/callback?code=test-code&state={authorizeQuery["state"]}");
    }

    private static string IdToken(string sub, string nonce) =>
        new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = ClientId,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = sub,
                    ["socialno"] = NationalId,
                    ["nonce"] = nonce,
                    ["name"] = DisplayName,
                    ["given_name"] = "Kari",
                    ["family_name"] = "Nordmann",
                    ["birthdate"] = "1946-03-27",
                    ["uniqueuserid"] = "9578-6000-4-351726",
                    ["certsubject"] = "CN=Nordmann\\, Kari,O=TestBank1 AS,C=NO",
                    ["phone_number"] = "+4712345678",
                },
                SigningCredentials = new SigningCredentials(
                    SigningKey,
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );
}
