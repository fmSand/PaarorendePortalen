using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Dtos.Kinship;
using Parorendeportalen.Api.Dtos.Visits;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Pipeline;

[Collection(PostgresCollection.Name)]
public class DemoLoginPipelineTests(PostgresContainerFixture fixture) : IAsyncLifetime
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

    [Fact]
    public async Task Health_AnswersWithoutAnyIduraSetting()
    {
        using var client = _app.CreateSecureClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_IsTheSeededDemoNextOfKin()
    {
        using var client = _app.CreateSecureClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<NextOfKinResponse>(PipelineApi.Json);
        Assert.NotNull(me);
        Assert.Equal("Demo Pårørende", me.DisplayName);
        Assert.Equal(
            ["Tor Quist", "Vigdis Quist"],
            me.Grants.Select(grant => grant.CareRecipientName).Order()
        );
    }

    [Fact]
    public async Task CareRecipients_AreTheOnesTheDemoNextOfKinWasGranted()
    {
        using var client = _app.CreateSecureClient();

        var response = await client.GetAsync("/api/carerecipients");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var careRecipients = await response.Content.ReadFromJsonAsync<List<CareRecipientResponse>>(
            PipelineApi.Json
        );
        Assert.NotNull(careRecipients);
        Assert.Equal(
            ["Tor Quist", "Vigdis Quist"],
            careRecipients.Select(careRecipient => careRecipient.Name).Order()
        );
    }

    [Fact]
    public async Task Visits_AnswerForACareRecipientTheDemoNextOfKinCanRead()
    {
        using var client = _app.CreateSecureClient();
        var careRecipientId = await client.FirstCareRecipientIdAsync();

        var response = await client.GetAsync($"/api/visits?careRecipientId={careRecipientId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResponse<VisitResponse>>(
            PipelineApi.Json
        );
        Assert.NotNull(page);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, visit => Assert.Equal(careRecipientId, visit.CareRecipientId));
    }

    [Theory]
    [InlineData("/api/auth/me", "/api/auth/me")]
    [InlineData("https://evil.example/", "/")]
    public async Task Login_RedirectsStraightBackToALocalReturnUrl(
        string returnUrl,
        string expected
    )
    {
        using var client = _app.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
            }
        );

        var response = await client.GetAsync(
            $"/api/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}"
        );

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }
}
