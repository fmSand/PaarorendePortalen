using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Data;

// The index these cover is what makes the sync upsert idempotent.
[Collection(PostgresCollection.Name)]
public class VisitProvenanceConstraintTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string IndexName = "IX_Visits_Origin_ExternalId";

    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task SavingTwoVisitsWithTheSameExternalIdAndOrigin_ViolatesTheUniqueIndex()
    {
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };

        using var context = _factory.CreateContext();
        context.CareRecipients.Add(vigdis);
        context.Visits.AddRange(
            NewVisit(vigdis, Origin.Synthetic, "visit-0001"),
            NewVisit(vigdis, Origin.Synthetic, "visit-0001")
        );

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync()
        );

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(IndexName, postgresException.ConstraintName);
    }

    // Passes with the filter removed too (Postgres treats NULLs as distinct)
    // pins behaviour (not filter clause).
    [Fact]
    public async Task SavingManyPortalVisitsWithoutAnExternalId_IsAllowed()
    {
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(vigdis);
            seedContext.Visits.AddRange(
                NewVisit(vigdis, Origin.Portal, externalId: null),
                NewVisit(vigdis, Origin.Portal, externalId: null),
                NewVisit(vigdis, Origin.Portal, externalId: null)
            );

            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();

        Assert.Equal(3, await context.Visits.CountAsync(v => v.Origin == Origin.Portal));
    }

    // Without this, an index on ExternalId alone would satisfy the other two tests.
    [Fact]
    public async Task SavingTheSameExternalIdUnderDifferentOrigins_IsAllowed()
    {
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };

        using (var seedContext = _factory.CreateContext())
        {
            seedContext.CareRecipients.Add(vigdis);
            seedContext.Visits.AddRange(
                NewVisit(vigdis, Origin.Synthetic, "shared-id"),
                NewVisit(vigdis, Origin.Portal, "shared-id")
            );

            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();
        var origins = await context
            .Visits.Where(v => v.ExternalId == "shared-id")
            .Select(v => v.Origin)
            .ToListAsync();

        Assert.Equal([Origin.Portal, Origin.Synthetic], origins.Order());
    }

    private static Visit NewVisit(CareRecipient careRecipient, Origin origin, string? externalId) =>
        new()
        {
            CareRecipient = careRecipient,
            ScheduledAt = DateTimeOffset.UtcNow,
            Status = VisitStatus.Planned,
            Origin = origin,
            ExternalId = externalId,
        };
}
