using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parorendeportalen.Api.Authentication;
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
        var demo = await db.NextOfKin.SingleAsync(n =>
            n.ExternalId == DemoAuthenticationHandler.ExternalId
        );
        var revoked = await db
            .Consents.Where(c =>
                c.NextOfKinId == demo.Id
                && c.CareRecipientId == careRecipientId
                && c.Category == category
            )
            .ExecuteDeleteAsync();
        Assert.Equal(1, revoked);

        using var request = await RequestAsync(client, route, careRecipientId);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var denial = Assert.Single(
            await db
                .AccessLogEntries.Where(e => e.Outcome == AccessDecision.DeniedNoConsent)
                .ToListAsync()
        );
        var operation =
            request.Method == HttpMethod.Get ? AccessOperation.Read : AccessOperation.Write;
        Assert.Equal(
            (demo.Id, careRecipientId, category, operation),
            (denial.NextOfKinId, denial.CareRecipientId, denial.Category, denial.Operation)
        );
    }

    // Row ids arbitrary: policy answers before any lookup.
    private static async Task<HttpRequestMessage> RequestAsync(
        HttpClient client,
        string route,
        int careRecipientId
    )
    {
        var parts = route.Split(' ');
        var method = new HttpMethod(parts[0]);
        var path = Regex.Replace(parts[1], @"\{[^}]+\}", "1");
        var request = new HttpRequestMessage(method, $"/{path}?careRecipientId={careRecipientId}");

        if (HealthData[route].Body is { } body)
        {
            request.Content = JsonContent.Create(body(careRecipientId), options: PipelineApi.Json);
        }

        if (method != HttpMethod.Get)
        {
            request.Headers.Add("X-XSRF-TOKEN", await client.TokenAsync());
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
