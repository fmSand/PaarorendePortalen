using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Extensions;
using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Sync;
using Parorendeportalen.Api.Integrations.Synthetic;
using Parorendeportalen.Api.Middleware;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;

var builder = WebApplication.CreateBuilder(args);

//Add services to the container

builder
    .Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
        options.JsonSerializerOptions.AllowDuplicateProperties = false;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

//https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
);

builder.Services.AddScoped<IVisitRepository, EfVisitRepository>();
builder.Services.AddScoped<IVisitService, VisitService>();
builder.Services.AddScoped<ICareRecipientRepository, EfCareRecipientRepository>();
builder.Services.AddScoped<ICareRecipientService, CareRecipientService>();
builder.Services.AddScoped<IKinshipRegistry, EfKinshipRegistry>();
builder.Services.AddScoped<INextOfKinService, NextOfKinService>();

var nationalIdPepper =
    builder.Configuration["Kinship:NationalIdPepper"]
    ?? throw new InvalidOperationException(
        "Kinship:NationalIdPepper is not configured — set it via user-secrets, never appsettings.json."
    );
builder.Services.AddSingleton(new NationalIdHasher(nationalIdPepper));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentNextOfKinAccessor, CurrentNextOfKinAccessor>();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<IConsentRepository, EfConsentRepository>();
builder.Services.AddScoped<IConsentService, ConsentService>();
builder.Services.AddScoped<IAccessLogRepository, EfAccessLogRepository>();
builder.Services.AddScoped<IHealthDataAccessPolicy, HealthDataAccessPolicy>();

builder.Services.AddScoped<IVisitIngestionStore, EfVisitIngestionStore>();
builder.Services.AddScoped<ISyncStateStore, EfSyncStateStore>();
builder.Services.AddScoped<IVisitSyncService, VisitSyncService>();

var visitSyncOptions =
    builder.Configuration.GetSection(VisitSyncOptions.SectionName).Get<VisitSyncOptions>()
    ?? new VisitSyncOptions();

if (visitSyncOptions.Enabled)
{
    // One worker per source, so a source that is down cannot delay another
    // source's data. A second source is a second registration here.
    var syntheticRecipients = CareRecipientSeedReader
        .Read(builder.Configuration)
        .Select(seed => new SyntheticRecipient(seed.Key, seed.NationalIdentifier))
        .ToList();

    builder.Services.AddHostedService(serviceProvider => new VisitSyncWorker(
        new SyntheticVisitSource(
            syntheticRecipients,
            serviceProvider.GetRequiredService<TimeProvider>()
        ),
        serviceProvider.GetRequiredService<IServiceScopeFactory>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        visitSyncOptions,
        serviceProvider.GetRequiredService<ILogger<VisitSyncWorker>>()
    ));
}

// RFC 7807 Problem Details
builder.Services.AddProblemDetails();

// CSRF layer 3 of 3. Registered now; wire it to first POST/PUT that isn't already covered by the OIDC/cookie flow
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "XSRF-TOKEN";
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddApiRateLimiting();

builder.Services.AddKinshipAuthentication(builder.Configuration, builder.Environment);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<NationalIdHasher>();
    var seedLogger = scope
        .ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger(typeof(DbSeeder));
    db.Database.Migrate();
    DbSeeder.BackfillCareRecipientIdentities(db, hasher, builder.Configuration, seedLogger);
    DbSeeder.SeedIfEmpty(db, hasher, builder.Configuration, app.Environment);
}

//Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // Dangerous on localhost - would force HTTPS on anything else run on this port later. Production only
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseSecurityHeaders();

app.UseHttpsRedirection();

// Before CSRF/auth - rejects abusive traffic before either costs anything
app.UseRateLimiter();

app.UseSecFetchSiteProtection();

app.UseAuthentication();
app.UseAuthorization();

// needed explicitly since the fallback policy makes everything private by default
app.MapHealthChecks("/health").AllowAnonymous();

app.MapControllers();

app.Run();
