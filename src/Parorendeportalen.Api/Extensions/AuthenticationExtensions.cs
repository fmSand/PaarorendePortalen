using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Parorendeportalen.Api.Authentication;

namespace Parorendeportalen.Api.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddKinshipAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment
    )
    {
        services.AddScoped<LoginValidator>();

        var isDemoEnvironment = environment.EnvironmentName == "Demo";

        var authenticationBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = isDemoEnvironment
                ? "Demo"
                : CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = isDemoEnvironment
                ? "Demo"
                : CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = isDemoEnvironment
                ? "Demo"
                : OpenIdConnectDefaults.AuthenticationScheme;
        });

        // Only registered under Demo - no code path reaches this handler in Development/Production
        if (isDemoEnvironment)
        {
            authenticationBuilder.AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(
                "Demo",
                _ => { }
            );
        }

        authenticationBuilder
            .AddCookie(options => ConfigureCookie(options, environment))
            .AddOpenIdConnect(options => ConfigureOpenIdConnect(options, configuration));

        return services;
    }

    private static void ConfigureCookie(
        CookieAuthenticationOptions options,
        IWebHostEnvironment environment
    )
    {
        options.Cookie.Name = environment.IsDevelopment() ? "pp.session" : "__Host-pp.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    }

    private static void ConfigureOpenIdConnect(
        OpenIdConnectOptions options,
        IConfiguration configuration
    )
    {
        options.ClientId =
            configuration["Idura:ClientId"]
            ?? throw new InvalidOperationException("Idura:ClientId is not configured.");
        options.ClientSecret =
            configuration["Idura:ClientSecret"]
            ?? throw new InvalidOperationException(
                "Idura:ClientSecret is not configured — set it via user-secrets, never appsettings.json."
            );
        options.Authority =
            $"https://{configuration["Idura:Domain"]
            ?? throw new InvalidOperationException("Idura:Domain is not configured.")}/";
        options.ResponseType = "code";

        options.ResponseMode = "query";

        options.CallbackPath = new PathString("/callback");
        options.SignedOutCallbackPath = new PathString("/signout");

        options.MapInboundClaims = false;

        options.Scope.Add("ssn");

        options.Events = OidcEvents.Create();
    }
}
