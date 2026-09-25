using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parorendeportalen.Api.Authentication;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Dtos.Visits;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Pipeline;

// Policy call lives in each action; this is what notices one going missing.
// New endpoint fails the inventory until listed in one of the two.
[Collection(PostgresCollection.Name)]
public class HealthDataEndpointTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private sealed record HealthDataEndpoint(
        DataCategory[] Categories,
        Func<int, object>? Body = null
    );

    private static readonly Dictionary<string, HealthDataEndpoint> HealthData = new()
    {
        ["GET api/Visits"] = new([DataCategory.Visits]),
        ["GET api/Visits/{id:int}"] = new([DataCategory.Visits]),
        ["POST api/Visits"] = new([DataCategory.Visits], NewVisit),
        ["PUT api/Visits/{id:int}"] = new([DataCategory.Visits], ChangedVisit),
        ["DELETE api/Visits/{id:int}"] = new([DataCategory.Visits]),
        ["GET api/visits/{visitId:int}/comments"] = new([DataCategory.Visits]),
        ["POST api/visits/{visitId:int}/comments"] = new([DataCategory.Visits], NewComment),
        ["PUT api/visits/{visitId:int}/comments/{id:int}"] = new(
            [DataCategory.Visits],
            ChangedComment
        ),
        ["DELETE api/visits/{visitId:int}/comments/{id:int}"] = new([DataCategory.Visits]),
        ["GET api/Vedtak"] = new([DataCategory.Vedtak]),
        ["GET api/Vedtak/{id:int}"] = new([DataCategory.Vedtak]),
        ["GET api/DayPlan"] = new([DataCategory.Vedtak, DataCategory.Visits]),
    };

    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["* /health"] = "liveness",
        ["GET api/antiforgery/token"] = "session plumbing",
        ["GET api/auth/login"] = "session plumbing",
        ["GET api/auth/me"] = "the caller's own identity",
        ["POST api/auth/logout"] = "session plumbing",
        ["GET api/CareRecipients"] = "names only, scoped by kinship",
        ["GET api/CareRecipients/{id:int}"] = "names only, scoped by kinship",
        ["GET api/Consents"] = "reports on access, reads no health data",
        ["GET api/Notifications"] =
            "health data, scope and log rows from AuthorizeConsentedReadsAsync",
        ["POST api/Notifications/{id:long}/read"] = "reads no health data, scope from the policy",
        ["POST api/Notifications/read"] = "reads no health data, scope from the policy",
        ["GET api/Notifications/preferences"] = "the caller's own settings",
        ["PUT api/Notifications/preferences/{kind}"] = "the caller's own settings",
    };

    public static TheoryData<string, DataCategory> HealthDataChecks
    {
        get
        {
            var checks = new TheoryData<string, DataCategory>();
            foreach (var (route, endpoint) in HealthData)
            {
                foreach (var category in endpoint.Categories)
                {
                    checks.Add(route, category);
                }
            }

            return checks;
        }
    }

    public static TheoryData<string> HealthDataRoutes => new(HealthData.Keys);

    // POST api/Visits reads careRecipientId from the body.
    public static TheoryData<string> ScopedByQuery =>
        new(HealthData.Keys.Where(route => route != "POST api/Visits").Append("GET api/Consents"));

    private sealed record Rows(
        int VisitId,
        uint VisitVersion,
        int CommentId,
        uint CommentVersion,
        int VedtakId
    );

    // Answered before any lookup.
    private static readonly Rows AnyRows = new(1, 1, 1, 1, 1);

    private PortalApplicationFactory _app = null!;
    private PostgresTestDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await PostgresTestDatabase.CreateWithoutSchemaAsync(fixture.ConnectionString);
        _app = new PortalApplicationFactory(_database.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public void EveryEndpoint_IsListedAsHealthDataOrExempt()
    {
        var mapped = _app
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Select(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
                return $"{string.Join(",", methods ?? ["*"])} {endpoint.RoutePattern.RawText}";
            })
            .Order(StringComparer.Ordinal);

        var listed = HealthData.Keys.Concat(Exempt.Keys).Order(StringComparer.Ordinal);

        Assert.Equal(listed, mapped);
    }

    // The denials below mean nothing unless a request let through would succeed.
    [Theory]
    [MemberData(nameof(HealthDataRoutes))]
    public async Task AHealthDataEndpoint_WithKinshipAndConsent_Succeeds(string route)
    {
        using var client = _app.CreateSecureClient();
        var careRecipientId = await client.FirstCareRecipientIdAsync();
        await using var db = _database.CreateContext();
        var rows = await ArrangeRowsAsync(client, db, careRecipientId);

        using var request = await RequestAsync(client, route, careRecipientId, rows);
        var response = await client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode, $"{route} answered {response.StatusCode}");
    }

    [Theory]
    [MemberData(nameof(HealthDataChecks))]
    public async Task AHealthDataEndpoint_WithoutConsentToACategory_AnswersForbidden_AndLogsTheDenial(
        string route,
        DataCategory category
    )
    {
        using var client = _app.CreateSecureClient();
        var careRecipientId = await client.FirstCareRecipientIdAsync();
        await using var db = _database.CreateContext();
        var rows = await ArrangeRowsAsync(client, db, careRecipientId);

        var demoId = await DemoIdAsync(db);
        var revoked = await db
            .Consents.Where(c =>
                c.NextOfKinId == demoId
                && c.CareRecipientId == careRecipientId
                && c.Category == category
            )
            .ExecuteDeleteAsync();
        Assert.Equal(1, revoked);
        var before = await WritableRowsAsync(db);

        using var request = await RequestAsync(client, route, careRecipientId, rows);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            (demoId, careRecipientId, category, OperationOf(request)),
            await SingleDenialAsync(db, AccessDecision.DeniedNoConsent)
        );
        Assert.Equal(before, await WritableRowsAsync(db));
    }

    [Theory]
    [MemberData(nameof(HealthDataRoutes))]
    public async Task AHealthDataEndpoint_WithoutKinshipToTheCareRecipient_AnswersNotFound_AndLogsTheDenial(
        string route
    )
    {
        using var client = _app.CreateSecureClient();
        var careRecipientId = await client.FirstCareRecipientIdAsync();
        await using var db = _database.CreateContext();
        var rows = await ArrangeRowsAsync(client, db, careRecipientId);

        var demoId = await DemoIdAsync(db);
        var revoked = await db
            .KinshipGrants.Where(g =>
                g.NextOfKinId == demoId && g.CareRecipientId == careRecipientId
            )
            .ExecuteDeleteAsync();
        Assert.Equal(1, revoked);
        var before = await WritableRowsAsync(db);

        using var request = await RequestAsync(client, route, careRecipientId, rows);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // DayPlan stops at its first category.
        Assert.Equal(
            (demoId, careRecipientId, HealthData[route].Categories[0], OperationOf(request)),
            await SingleDenialAsync(db, AccessDecision.DeniedNoKinship)
        );
        Assert.Equal(before, await WritableRowsAsync(db));
    }

    [Theory]
    [MemberData(nameof(ScopedByQuery))]
    public async Task AnEndpointScopedByQuery_WithoutCareRecipientId_AnswersBadRequest_BeforeAskingThePolicy(
        string route
    )
    {
        using var client = _app.CreateSecureClient();

        using var request = await RequestAsync(client, route, careRecipientId: null, AnyRows);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(
            PipelineApi.Json
        );
        Assert.NotNull(problem);
        Assert.Equal("careRecipientId", Assert.Single(problem.Errors).Key);

        await using var db = _database.CreateContext();
        Assert.Equal(0, await db.AccessLogEntries.CountAsync());
    }

    private static async Task<Rows> ArrangeRowsAsync(
        HttpClient client,
        AppDbContext db,
        int careRecipientId
    )
    {
        var visit = await CreateAsync<VisitResponse>(
            client,
            "/api/visits",
            NewVisit(careRecipientId)
        );
        var comment = await CreateAsync<VisitCommentResponse>(
            client,
            $"/api/visits/{visit.Id}/comments?careRecipientId={careRecipientId}",
            NewComment(careRecipientId)
        );
        var vedtakId = await db
            .Vedtak.Where(v => v.CareRecipientId == careRecipientId)
            .Select(v => v.Id)
            .FirstAsync();

        return new Rows(visit.Id, visit.Version, comment.Id, comment.Version, vedtakId);
    }

    private static async Task<T> CreateAsync<T>(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: PipelineApi.Json),
        };
        request.Headers.Add("X-XSRF-TOKEN", await client.TokenAsync());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<T>(PipelineApi.Json);
        Assert.NotNull(created);
        return created;
    }

    private static Task<int> DemoIdAsync(AppDbContext db) =>
        db
            .NextOfKin.Where(n => n.ExternalId == DemoAuthenticationHandler.ExternalId)
            .Select(n => n.Id)
            .SingleAsync();

    private static async Task<(int, int, DataCategory, AccessOperation)> SingleDenialAsync(
        AppDbContext db,
        AccessDecision outcome
    )
    {
        var denial = Assert.Single(
            await db.AccessLogEntries.Where(e => e.Outcome == outcome).ToListAsync()
        );
        return (denial.NextOfKinId, denial.CareRecipientId, denial.Category, denial.Operation);
    }

    // Tables the HealthData writes store into; a write that ran before the refusal changes one.
    private static async Task<string[]> WritableRowsAsync(AppDbContext db)
    {
        var visits = await db
            .Visits.OrderBy(v => v.Id)
            .Select(v => new { v.Id, v.Version })
            .ToListAsync();
        var comments = await db
            .VisitComments.OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Version })
            .ToListAsync();

        return
        [
            .. visits.Select(v => $"visit {v.Id} v{v.Version}"),
            .. comments.Select(c => $"comment {c.Id} v{c.Version}"),
        ];
    }

    private static AccessOperation OperationOf(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get ? AccessOperation.Read : AccessOperation.Write;

    private static async Task<HttpRequestMessage> RequestAsync(
        HttpClient client,
        string route,
        int? careRecipientId,
        Rows rows
    )
    {
        var parts = route.Split(' ');
        var method = new HttpMethod(parts[0]);
        var (id, version) =
            parts[1].Contains("/comments/", StringComparison.Ordinal)
                ? (rows.CommentId, rows.CommentVersion)
            : parts[1].StartsWith("api/Vedtak", StringComparison.Ordinal) ? (rows.VedtakId, 0u)
            : (rows.VisitId, rows.VisitVersion);
        var path = parts[1]
            .Replace("{visitId:int}", $"{rows.VisitId}", StringComparison.Ordinal)
            .Replace("{id:int}", $"{id}", StringComparison.Ordinal);
        var query = careRecipientId is null ? "" : $"?careRecipientId={careRecipientId}";
        var request = new HttpRequestMessage(method, $"/{path}{query}");

        // Only POST api/Visits reads the id, and it always gets one.
        if (HealthData.GetValueOrDefault(route)?.Body is { } body)
        {
            request.Content = JsonContent.Create(
                body(careRecipientId ?? 0),
                options: PipelineApi.Json
            );
        }

        if (method != HttpMethod.Get)
        {
            request.Headers.Add("X-XSRF-TOKEN", await client.TokenAsync());
        }

        if (method == HttpMethod.Put || method == HttpMethod.Delete)
        {
            request.Headers.Add("If-Match", $"\"{version}\"");
        }

        return request;
    }

    private static readonly DateTimeOffset ScheduledAt = new(2027, 6, 15, 8, 0, 0, TimeSpan.Zero);

    private static CreateVisitRequest NewVisit(int careRecipientId) =>
        new()
        {
            CareRecipientId = careRecipientId,
            ScheduledAt = ScheduledAt,
            Title = "Legetime",
            Visibility = Visibility.Shared,
        };

    private static UpdateVisitRequest ChangedVisit(int _) =>
        new()
        {
            ScheduledAt = ScheduledAt,
            Title = "Legetime",
            Visibility = Visibility.Shared,
        };

    private static CreateVisitCommentRequest NewComment(int _) =>
        new() { Body = "Takk", Visibility = Visibility.Shared };

    private static UpdateVisitCommentRequest ChangedComment(int _) =>
        new() { Body = "Takk", Visibility = Visibility.Shared };
}
