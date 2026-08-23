using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;

var builder = WebApplication.CreateBuilder(args);

//Add services to the container

//https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IKinshipRegistry, EfKinshipRegistry>();
builder.Services.AddScoped<INextOfKinService, NextOfKinService>();

var nationalIdPepper = builder.Configuration["Kinship:NationalIdPepper"]
    ?? throw new InvalidOperationException("Kinship:NationalIdPepper is not configured — set it via user-secrets, never appsettings.json.");
builder.Services.AddSingleton(new NationalIdHasher(nationalIdPepper));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentNextOfKinAccessor, CurrentNextOfKinAccessor>();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var isDemoEnvironment = builder.Environment.EnvironmentName == "Demo";

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = isDemoEnvironment ? "Demo" : CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = isDemoEnvironment ? "Demo" : CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = isDemoEnvironment ? "Demo" : OpenIdConnectDefaults.AuthenticationScheme;
});

authenticationBuilder
.AddCookie(options =>
{
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "pp.session" : "__Host-pp.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
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
})
.AddOpenIdConnect(options =>
{
    options.ClientId = builder.Configuration["Idura:ClientId"]
        ?? throw new InvalidOperationException("Idura:ClientId is not configured.");
    options.ClientSecret = builder.Configuration["Idura:ClientSecret"]
        ?? throw new InvalidOperationException("Idura:ClientSecret is not configured — set it via user-secrets, never appsettings.json.");
    options.Authority = $"https://{builder.Configuration["Idura:Domain"]
        ?? throw new InvalidOperationException("Idura:Domain is not configured.")}/";
    options.ResponseType = "code";

    options.ResponseMode = "query";

    options.CallbackPath = new PathString("/callback");
    options.SignedOutCallbackPath = new PathString("/signout");

    options.MapInboundClaims = false;

    options.Scope.Add("ssn");

    options.Events = new OpenIdConnectEvents
    {
        OnTokenValidated = async context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

            var externalId = context.Principal?.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(externalId))
            {
                logger.LogWarning("OIDC token validated with no 'sub' claim — rejecting login.");
                context.Fail(KinshipFailureReasons.NoSubClaim);
                return;
            }

            var nationalId = context.Principal?.FindFirst("socialno")?.Value;
            if (string.IsNullOrEmpty(nationalId))
            {
                logger.LogWarning("OIDC token validated with no 'socialno' claim — rejecting login.");
                context.Fail(KinshipFailureReasons.NoSocialNoClaim);
                return;
            }

            var displayName = context.Principal?.FindFirst("name")?.Value ?? externalId;

            var nextOfKinService = context.HttpContext.RequestServices.GetRequiredService<INextOfKinService>();
            var nextOfKin = await nextOfKinService.ResolveOrBindAsync(
                externalId, nationalId, displayName, context.HttpContext.RequestAborted);

            if (nextOfKin is null)
            {
                logger.LogWarning("OIDC login with no registered kinship grant — rejecting login.");
                context.Fail(KinshipFailureReasons.NoGrant);
                return;
            }

            logger.LogInformation("NextOfKin {NextOfKinId} authenticated via OIDC.", nextOfKin.Id);
        },

        OnRemoteFailure = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";

            var detail = context.Failure?.Message is string message && KinshipFailureReasons.All.Contains(message)
                ? message
                : "Login failed.";

            return context.Response.WriteAsJsonAsync(new { title = "Login failed.", detail, status = StatusCodes.Status403Forbidden });
        },
    };
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<NationalIdHasher>();
    db.Database.Migrate();
    DbSeeder.SeedIfEmpty(db, hasher, builder.Configuration, app.Environment);
}

//Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

internal static class KinshipFailureReasons
{
    public const string NoSubClaim = "Idura did not return a 'sub' claim.";
    public const string NoSocialNoClaim = "Idura did not return a 'socialno' claim — SSN scope/consent may not be configured.";
    public const string NoGrant = "No registered next-of-kin relation for this identity.";

    public static readonly string[] All = [NoSubClaim, NoSocialNoClaim, NoGrant];
}
