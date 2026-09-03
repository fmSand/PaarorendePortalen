using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public class EfAccessLogRepositoryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private static AccessLogEntry NewEntry(
        AccessDecision outcome = AccessDecision.Granted,
        int nextOfKinId = 5,
        int careRecipientId = 7
    ) =>
        new()
        {
            OccurredAt = Snapshots.Noon,
            NextOfKinId = nextOfKinId,
            CareRecipientId = careRecipientId,
            Category = DataCategory.Visits,
            Outcome = outcome,
        };

    [Fact]
    public async Task AppendAsync_PersistsEveryField()
    {
        using (var context = _factory.CreateContext())
        {
            var sut = new EfAccessLogRepository(context);
            await sut.AppendAsync(NewEntry(AccessDecision.DeniedNoConsent), CancellationToken.None);
        }

        using var verifyContext = _factory.CreateContext();
        var persisted = await verifyContext.AccessLogEntries.SingleAsync();

        Assert.Equal(Snapshots.Noon, persisted.OccurredAt);
        Assert.Equal(5, persisted.NextOfKinId);
        Assert.Equal(7, persisted.CareRecipientId);
        Assert.Equal(DataCategory.Visits, persisted.Category);
        Assert.Equal(AccessDecision.DeniedNoConsent, persisted.Outcome);
    }

    [Fact]
    public async Task AppendAsync_AddsARow_EveryTime()
    {
        using (var context = _factory.CreateContext())
        {
            var sut = new EfAccessLogRepository(context);
            await sut.AppendAsync(NewEntry(), CancellationToken.None);
            await sut.AppendAsync(NewEntry(), CancellationToken.None);
        }

        using var verifyContext = _factory.CreateContext();

        Assert.Equal(2, await verifyContext.AccessLogEntries.CountAsync());
    }

    // No FKs, so a probe naming a care recipient who does not exist still logs.
    [Fact]
    public async Task AppendAsync_PersistsARow_WhoseIdsMatchNoPersonOrCareRecipient()
    {
        using (var context = _factory.CreateContext())
        {
            var sut = new EfAccessLogRepository(context);
            await sut.AppendAsync(
                NewEntry(AccessDecision.DeniedNoKinship, nextOfKinId: 404, careRecipientId: 404),
                CancellationToken.None
            );
        }

        using var verifyContext = _factory.CreateContext();

        Assert.Equal(1, await verifyContext.AccessLogEntries.CountAsync());
    }
}
