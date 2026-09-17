using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Dtos.Kinship;

namespace Parorendeportalen.Api.Tests.TestHelpers;

/// <summary>
/// What a pipeline test needs before it can reach an endpoint: The JSON settings
/// the API uses, antiforgery token, and a care recipient the demo session can read.
/// </summary>
internal static class PipelineApi
{
    public static JsonSerializerOptions Json { get; } =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<string> TokenAsync(this HttpClient client)
    {
        var response = await client.GetFromJsonAsync<AntiforgeryTokenResponse>(
            "/api/antiforgery/token",
            Json
        );

        Assert.NotNull(response);
        return response.Token;
    }

    public static async Task<int> FirstCareRecipientIdAsync(this HttpClient client)
    {
        var careRecipients = await client.GetFromJsonAsync<List<CareRecipientResponse>>(
            "/api/carerecipients",
            Json
        );

        Assert.NotNull(careRecipients);
        return careRecipients[0].Id;
    }
}
