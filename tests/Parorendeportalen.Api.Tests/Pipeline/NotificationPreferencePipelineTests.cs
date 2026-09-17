using System.Net;
using System.Net.Http.Json;
using System.Text;
using Parorendeportalen.Api.Dtos.Notifications;
using Parorendeportalen.Api.Models.Notifications;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Pipeline;

// Bodies are written out as text, since the point is what model binding and the
// input formatter do with JSON the request DTO cannot express.
[Collection(PostgresCollection.Name)]
public class NotificationPreferencePipelineTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const ChangeKind Kind = ChangeKind.Missed;

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettingAPreference_ReturnsNoContent_AndTheChoiceIsReadBack(bool enabled)
    {
        using var client = _app.CreateSecureClient();

        var response = await PutAsync(
            client,
            Kind,
            $$"""{"enabled":{{(enabled ? "true" : "false")}}}"""
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(enabled, await ReadPreferenceAsync(client, Kind));
    }

    [Fact]
    public async Task SettingAPreferenceTwice_KeepsTheLastChoice()
    {
        using var client = _app.CreateSecureClient();

        await PutAsync(client, Kind, """{"enabled":false}""");
        var response = await PutAsync(client, Kind, """{"enabled":true}""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await ReadPreferenceAsync(client, Kind));
    }

    // None of these states a choice. A default here would silently turn a notice back on.
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"enabled":null}""")]
    [InlineData("""{"enabled":"yes"}""")]
    [InlineData("""{"enabled":1}""")]
    public async Task ABodyThatDoesNotStateAChoice_IsRejected_AndLeavesTheStoredChoiceAlone(
        string body
    )
    {
        using var client = _app.CreateSecureClient();
        await PutAsync(client, Kind, """{"enabled":false}""");

        var response = await PutAsync(client, Kind, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await ReadPreferenceAsync(client, Kind));
    }

    [Theory]
    [InlineData("Sneezed")]
    [InlineData("42")]
    public async Task AKindThatDoesNotExist_IsRejected(string kind)
    {
        using var client = _app.CreateSecureClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/notifications/preferences/{kind}"
        )
        {
            Content = Body("""{"enabled":true}"""),
        };
        request.Headers.Add("X-XSRF-TOKEN", await client.TokenAsync());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client,
        ChangeKind kind,
        string body
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/notifications/preferences/{kind}"
        )
        {
            Content = Body(body),
        };
        request.Headers.Add("X-XSRF-TOKEN", await client.TokenAsync());

        return await client.SendAsync(request);
    }

    private static async Task<bool> ReadPreferenceAsync(HttpClient client, ChangeKind kind)
    {
        var preferences = await client.GetFromJsonAsync<List<NotificationPreferenceResponse>>(
            "/api/notifications/preferences",
            PipelineApi.Json
        );

        Assert.NotNull(preferences);
        return preferences.Single(preference => preference.Kind == kind).Enabled;
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");
}
