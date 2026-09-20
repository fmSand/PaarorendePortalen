using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Extensions;
using Parorendeportalen.Api.Filters;
using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Sync;
using Parorendeportalen.Api.Integrations.Synthetic;
using Parorendeportalen.Api.Middleware;
using Parorendeportalen.Api.Notifications;
using Parorendeportalen.Api.Repositories.Access;
using Parorendeportalen.Api.Repositories.Kinship;
using Parorendeportalen.Api.Repositories.Notifications;
using Parorendeportalen.Api.Repositories.Planning;
using Parorendeportalen.Api.Repositories.Visits;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Services.Access;
using Parorendeportalen.Api.Services.Kinship;
using Parorendeportalen.Api.Services.Notifications;
using Parorendeportalen.Api.Services.Planning;
using Parorendeportalen.Api.Services.Visits;

var builder = WebApplication.CreateBuilder(args);

//Add services to the container

builder
    .Services.AddControllers(options =>
    {
        // Registered globally, so a new write endpoint is covered without an attribute.
        options.Filters.Add<ValidateAntiforgeryFilter>();
    })
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
builder.Services.AddScoped<IVisitCommentRepository, EfVisitCommentRepository>();
builder.Services.AddScoped<IVisitCommentService, VisitCommentService>();
builder.Services.AddScoped<ICareRecipientRepository, EfCareRecipientRepository>();
builder.Services.AddScoped<ICareRecipientService, CareRecipientService>();
builder.Services.AddScoped<IKinshipRegistry, EfKinshipRegistry>();
builder.Services.AddScoped<INextOfKinService, NextOfKinService>();
builder.Services.AddScoped<IVedtakRepository, EfVedtakRepository>();
builder.Services.AddScoped<IVedtakService, VedtakService>();
builder.Services.AddScoped<IDayPlanService, DayPlanService>();

var configuredPepper = builder.Configuration["Kinship:NationalIdPepper"];

var nationalIdPepper =
    configuredPepper
    ?? (
        builder.Environment.IsDemo()
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            : throw new InvalidOperationException(
                "Kinship:NationalIdPepper is not configured. Set it in user-secrets and keep it out of appsettings.json."
            )
    );

builder.Services.AddSingleton(new NationalIdHasher(nationalIdPepper));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentNextOfKinAccessor, CurrentNextOfKinAccessor>();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<IConsentRepository, EfConsentRepository>();
builder.Services.AddScoped<IConsentService, ConsentService>();
builder.Services.AddScoped<IAccessLogRepository, EfAccessLogRepository>();
builder.Services.AddScoped<IHealthDataAccessPolicy, HealthDataAccessPolicy>();

builder.Services.AddScoped<INotificationRepository, EfNotificationRepository>();
builder.Services.AddScoped<INotificationPreferenceRepository, EfNotificationPreferenceRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IChangeEventStore, EfChangeEventStore>();
builder.Services.AddScoped<INotificationFanOut, NotificationFanOut>();

var notificationOptions =
    builder.Configuration.GetSection(NotificationOptions.SectionName).Get<NotificationOptions>()
    ?? new NotificationOptions();
builder.Services.AddSingleton(notificationOptions);

if (notificationOptions.Enabled)
{
    // The outbox consumer. One instance whatever the number of sources.
    builder.Services.AddHostedService<NotificationFanOutWorker>();
}

builder.Services.AddScoped<IVisitIngestionStore, EfVisitIngestionStore>();
builder.Services.AddScoped<ISyncStateStore, EfSyncStateStore>();
builder.Services.AddScoped<IVisitSyncService, VisitSyncService>();

var visitSyncOptions =
    builder.Configuration.GetSection(VisitSyncOptions.SectionName).Get<VisitSyncOptions>()
    ?? new VisitSyncOptions();

if (visitSyncOptions.Enabled)
{
    // A second source is a second registration here.
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

builder.Services.AddCsrfProtection(builder.Environment);

builder.Services.AddKinshipAuthorization();

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddApiRateLimiting();

builder.Services.AddKinshipAuthentication(builder.Configuration, builder.Environment);

// Resolve the time zone before the host starts. Left to the first day plan, a
// failing static initializer arrives as a 500 with the reason wrapped a level
// down, and every later request gets the same cached failure.
_ = NorwegianTime.Zone;

var app = builder.Build();

if (configuredPepper is null)
{
    app.Logger.LogWarning(
        "Kinship:NationalIdPepper is not configured. This Demo run generated one, so identifier hashes change on every restart."
    );
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<NationalIdHasher>();
    var seedLogger = scope
        .ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger(typeof(DbSeeder));
    db.Database.Migrate();
    DbSeeder.BackfillCareRecipientIdentities(db, hasher, builder.Configuration, seedLogger);
    DbSeeder.BackfillVedtak(db);
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
