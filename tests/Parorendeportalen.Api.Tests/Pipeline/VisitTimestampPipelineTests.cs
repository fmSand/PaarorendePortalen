using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Dtos.Visits;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Pipeline;

// Bodies and query strings are written out as text. Building a request record here would
// convert the timestamp on this side and the offset would never reach the API.
[Collection(PostgresCollection.Name)]
public class VisitTimestampPipelineTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset OsloMorning = new(
        2027,
        6,
        15,
        8,
        0,
        0,
        TimeSpan.FromHours(2)
    );

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
    public async Task CreatingAVisitOnAnOsloOffset_StoresTheInstantItNames()
    {
        using var client = _app.CreateSecureClient();
        var careRecipientId = await client.FirstCareRecipientIdAsync();

        var created = await CreateAsync(client, careRecipientId, OsloMorning, "Legetime");

        Assert.Equal(OsloMorning, created.ScheduledAt);
        Assert.Equal(TimeSpan.Zero, created.ScheduledAt.Offset);
    }

    [Fact]
    public async Task MovingAVisitToATimeOnAnOsloOffset_StoresTheInstantItNames()
    {
        using var client = _app.CreateSecureClient();
        var careRecipientId = await client.FirstCareRecipientIdAsync();
        var created = await CreateAsync(
            client,
            careRecipientId,
            OsloMorning.ToUniversalTime(),
            "Legetime"
        );
        var moved = OsloMorning.AddHours(3);

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/visits/{created.Id}?careRecipientId={careRecipientId}"
        )
        {
            Content = Body(
                $$"""
                {"scheduledAt":"{{moved:O}}","title":"Legetime, flyttet","visibility":"Shared"}
                """
            ),
        };
        request.Headers.Add("X-XSRF-TOKEN", await client.TokenAsync());
        request.Headers.Add("If-Match", $"\"{created.Version}\"");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<VisitResponse>(PipelineApi.Json);
        Assert.NotNull(updated);
        Assert.Equal(moved, updated.ScheduledAt);
        Assert.Equal(TimeSpan.Zero, updated.ScheduledAt.Offset);
    }

    [Fact]
    public async Task ListingADayWrittenOnAnOsloOffset_KeepsTheVisitsThatFallInsideIt()
    {
        using var client = _app.CreateSecureClient();
        var careRecipientId = await client.FirstCareRecipientIdAsync();
        var dayStart = new DateTimeOffset(2027, 6, 15, 0, 0, 0, TimeSpan.FromHours(2));

        // Written in UTC, so only the query carries an offset.
        await CreateAsync(client, careRecipientId, OsloMorning.ToUniversalTime(), "inside the day");
        await CreateAsync(
            client,
            careRecipientId,
            dayStart.AddDays(1).AddMinutes(30).ToUniversalTime(),
            "after midnight"
        );

        var response = await client.GetAsync(
            $"/api/visits?careRecipientId={careRecipientId}"
                + $"&from={Query(dayStart)}&to={Query(dayStart.AddDays(1))}"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResponse<VisitResponse>>(
            PipelineApi.Json
        );
        Assert.NotNull(page);
        Assert.Equal(["inside the day"], page.Items.Select(visit => visit.Title));
    }

    private static async Task<VisitResponse> CreateAsync(
        HttpClient client,
        int careRecipientId,
        DateTimeOffset scheduledAt,
        string title
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/visits")
        {
            Content = Body(
                $$"""
                {"careRecipientId":{{careRecipientId}},"scheduledAt":"{{scheduledAt:O}}","title":"{{title}}","visibility":"Shared"}
                """
            ),
        };
        request.Headers.Add("X-XSRF-TOKEN", await client.TokenAsync());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<VisitResponse>(PipelineApi.Json);
        Assert.NotNull(created);
        return created;
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    // The plus in an offset is a space once the query string is decoded.
    private static string Query(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToString("O", CultureInfo.InvariantCulture));
}
