using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Dtos.Kinship;
using Parorendeportalen.Api.Dtos.Visits;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Pipeline;

// Runs the whole pipeline, so a missing filter registration shows up here.
[Collection(PostgresCollection.Name)]
public class AntiforgeryPipelineTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

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
    public async Task UnsafeRequestWithoutAToken_IsRejectedBeforeTheActionRuns()
    {
        using var client = _app.CreateSecureClient();

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnsafeRequestWithATokenFromTheTokenEndpoint_IsLetThrough()
    {
        using var client = _app.CreateSecureClient();

        var token = await TokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-XSRF-TOKEN", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreatingAVisit_NeedsTheTokenAndThenSucceedsEndToEnd()
    {
        using var client = _app.CreateSecureClient();

        var careRecipientId = await FirstCareRecipientIdAsync(client);
        var body = new CreateVisitRequest
        {
            CareRecipientId = careRecipientId,
            ScheduledAt = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            Title = "Legetime på Ullevål",
            Visibility = Visibility.Shared,
        };

        var withoutToken = await client.PostAsJsonAsync("/api/visits", body, Json);
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);

        var token = await TokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/visits")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        var created = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var visit = await created.Content.ReadFromJsonAsync<VisitResponse>(Json);
        Assert.NotNull(visit);
        Assert.Equal("Legetime på Ullevål", visit.Title);
        Assert.Equal(Origin.Portal, visit.Origin);
        Assert.Equal("Demo Pårørende", visit.CreatedByName);

        // Missing or empty ETag would make every later edit of this row impossible.
        Assert.Equal($"\"{visit.Version}\"", created.Headers.ETag?.ToString());
        Assert.NotEqual(0u, visit.Version);
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<AntiforgeryTokenResponse>(
            "/api/antiforgery/token",
            Json
        );

        Assert.NotNull(response);
        return response.Token;
    }

    private static async Task<int> FirstCareRecipientIdAsync(HttpClient client)
    {
        var careRecipients = await client.GetFromJsonAsync<List<CareRecipientResponse>>(
            "/api/carerecipients",
            Json
        );

        Assert.NotNull(careRecipients);
        return careRecipients[0].Id;
    }
}
