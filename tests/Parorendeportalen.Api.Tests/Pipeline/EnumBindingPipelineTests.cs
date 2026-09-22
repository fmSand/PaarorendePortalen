using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Pipeline;

// Bodies are written as text: a request record only holds a Visibility the enum defines.
[Collection(PostgresCollection.Name)]
public class EnumBindingPipelineTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
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

    // A stored undefined visibility hides the row, then another's update answers 403 and reveals it.
    [Theory]
    [InlineData("7")]
    [InlineData("\"7\"")]
    [InlineData("1")]
    public async Task AVisibilityWrittenAsDigits_IsRejected_AndNoVisitIsStored(string visibility)
    {
        using var client = _app.CreateSecureClient();

        var response = await PostVisitAsync(client, visibility);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(
            PipelineApi.Json
        );
        Assert.NotNull(problem);
        Assert.Contains("$.visibility", problem.Errors.Keys);

        await using var db = _database.CreateContext();
        Assert.Equal(0, await db.Visits.CountAsync(visit => visit.Origin == Origin.Portal));
    }

    [Fact]
    public async Task AVisibilityWrittenByName_IsAcceptedAndAnsweredByName()
    {
        using var client = _app.CreateSecureClient();

        var response = await PostVisitAsync(client, "\"Shared\"");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("\"visibility\":\"Shared\"", await response.Content.ReadAsStringAsync());

        await using var db = _database.CreateContext();
        var stored = await db.Visits.SingleAsync(visit => visit.Origin == Origin.Portal);
        Assert.Equal(Visibility.Shared, stored.Visibility);
    }

    private static async Task<HttpResponseMessage> PostVisitAsync(
        HttpClient client,
        string visibility
    )
    {
        var careRecipientId = await client.FirstCareRecipientIdAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/visits")
        {
            Content = new StringContent(
                $$"""
                {"careRecipientId":{{careRecipientId}},"scheduledAt":"2027-06-15T08:00:00Z","title":"Legetime","visibility":{{visibility}}}
                """,
                Encoding.UTF8,
                "application/json"
            ),
        };
        request.Headers.Add("X-XSRF-TOKEN", await client.TokenAsync());

        return await client.SendAsync(request);
    }
}
