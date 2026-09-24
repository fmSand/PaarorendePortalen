using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models.Kinship;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Data;

// The model-level half of the rule that a stored visit never moves to another care recipient.
// Ingestion holds the other half.
[Collection(PostgresCollection.Name)]
public class VisitCareRecipientImmutabilityTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task MovingAStoredVisitToAnotherCareRecipient_Throws()
    {
        var (visitId, torId) = await SeedAsync();

        using var context = _factory.CreateContext();
        var visit = await context.Visits.SingleAsync(v => v.Id == visitId);
        visit.CareRecipientId = torId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task EditingTheRestOfAStoredVisit_IsAllowed()
    {
        var (visitId, _) = await SeedAsync();

        using (var context = _factory.CreateContext())
        {
            var visit = await context.Visits.SingleAsync(v => v.Id == visitId);
            visit.Status = VisitStatus.Completed;
            await context.SaveChangesAsync();
        }

        using var readContext = _factory.CreateContext();
        var stored = await readContext.Visits.SingleAsync(v => v.Id == visitId);
        Assert.Equal(VisitStatus.Completed, stored.Status);
    }

    private async Task<(int VisitId, int TorId)> SeedAsync()
    {
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        var tor = new CareRecipient { Name = "Tor Quist" };
        var visit = new Visit
        {
            CareRecipient = vigdis,
            ScheduledAt = DateTimeOffset.UtcNow,
            Status = VisitStatus.Planned,
            Origin = Origin.Synthetic,
            ExternalId = "visit-0001",
        };

        using var context = _factory.CreateContext();
        context.CareRecipients.AddRange(vigdis, tor);
        context.Visits.Add(visit);
        await context.SaveChangesAsync();

        return (visit.Id, tor.Id);
    }
}
