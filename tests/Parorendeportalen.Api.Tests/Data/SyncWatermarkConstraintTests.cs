using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Sync;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Data;

// Two watermarks for one (source, resource type) would let two runs advance
// past each other's data.
[Collection(PostgresCollection.Name)]
public class SyncWatermarkConstraintTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string IndexName = "IX_SyncWatermarks_SourceSystem_ResourceType";

    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task TwoWatermarksForOneKey_ViolateTheUniqueIndex()
    {
        using var context = _factory.CreateContext();
        context.SyncWatermarks.AddRange(
            NewWatermark(SyncResourceType.Visit, Snapshots.Noon),
            NewWatermark(SyncResourceType.Visit, Snapshots.Noon.AddMinutes(1))
        );

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync()
        );

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(IndexName, postgresException.ConstraintName);
    }

    // Otherwise an index on SourceSystem alone would satisfy the test above.
    [Fact]
    public async Task OneWatermarkPerResourceType_IsAllowed()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.SyncWatermarks.AddRange(
                NewWatermark(SyncResourceType.Visit, Snapshots.Noon),
                NewWatermark((SyncResourceType)2, Snapshots.Noon)
            );
            await seedContext.SaveChangesAsync();
        }

        using var context = _factory.CreateContext();

        Assert.Equal(2, await context.SyncWatermarks.CountAsync());
    }

    private static SyncWatermark NewWatermark(
        SyncResourceType resourceType,
        DateTimeOffset through
    ) =>
        new()
        {
            SourceSystem = SourceSystem.Synthetic,
            ResourceType = resourceType,
            SourceUpdatedThrough = through,
        };
}
