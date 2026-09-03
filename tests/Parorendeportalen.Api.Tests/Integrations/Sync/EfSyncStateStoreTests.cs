using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Sync;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Integrations.Sync;

[Collection(PostgresCollection.Name)]
public class EfSyncStateStoreTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = Snapshots.Noon;

    // Vedtak and dagsplan come from the same systems as visits, so the key has
    // to separate them before there is a second value to separate.
    private const SyncResourceType ASecondResourceType = (SyncResourceType)2;

    private PostgresTestDatabase _factory = null!;

    public async Task InitializeAsync() =>
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private static EfSyncStateStore StoreOn(AppDbContext context) =>
        new(context, TimeProvider.System);

    private static VisitSyncOutcome Outcome(
        int inserted = 0,
        int updated = 0,
        int unchanged = 0,
        int unresolved = 0,
        DateTimeOffset? watermarkThrough = null,
        string? continuationToken = null,
        DateTimeOffset? unresolvedFrom = null,
        bool truncated = false
    ) =>
        new(
            new VisitIngestionResult(inserted, updated, unchanged),
            unresolved,
            watermarkThrough is null && continuationToken is null && unresolvedFrom is null
                ? null
                : new SyncPosition(watermarkThrough, continuationToken, unresolvedFrom),
            truncated
        );

    private async Task<int> StartRunAsync(SyncResourceType resourceType = SyncResourceType.Visit)
    {
        using var context = _factory.CreateContext();

        return await StoreOn(context)
            .StartRunAsync(SourceSystem.Synthetic, resourceType, CancellationToken.None);
    }

    private async Task<SyncPosition> GetPositionAsync(
        SyncResourceType resourceType = SyncResourceType.Visit
    )
    {
        using var context = _factory.CreateContext();

        return await StoreOn(context)
            .GetPositionAsync(SourceSystem.Synthetic, resourceType, CancellationToken.None);
    }

    private async Task CompleteRunAsync(int runId, VisitSyncOutcome outcome)
    {
        using var context = _factory.CreateContext();
        await StoreOn(context).CompleteRunAsync(runId, outcome, CancellationToken.None);
    }

    [Fact]
    public async Task NothingHasSyncedYet_MeansStartingFromTheBeginning()
    {
        Assert.Equal(SyncPosition.Start, await GetPositionAsync());
    }

    [Fact]
    public async Task AStartedRun_IsRecordedAsStillRunning()
    {
        var runId = await StartRunAsync();

        using var context = _factory.CreateContext();
        var run = await context.SyncRuns.SingleAsync(r => r.Id == runId);

        Assert.Equal(SyncRunStatus.Running, run.Status);
        Assert.Equal(SourceSystem.Synthetic, run.SourceSystem);
        Assert.Equal(SyncResourceType.Visit, run.ResourceType);
        Assert.Null(run.CompletedAt);
        Assert.Null(run.Error);
        Assert.False(run.Truncated);
    }

    [Fact]
    public async Task ACompletedRun_CarriesItsCounts()
    {
        var runId = await StartRunAsync();

        await CompleteRunAsync(
            runId,
            Outcome(inserted: 3, updated: 2, unchanged: 7, unresolved: 1, watermarkThrough: Noon)
        );

        using var context = _factory.CreateContext();
        var run = await context.SyncRuns.SingleAsync(r => r.Id == runId);

        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
        Assert.Equal(3, run.Inserted);
        Assert.Equal(2, run.Updated);
        Assert.Equal(7, run.Unchanged);
        Assert.Equal(1, run.Unresolved);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task TheFirstCompletedRun_CreatesTheWatermark()
    {
        await CompleteRunAsync(await StartRunAsync(), Outcome(watermarkThrough: Noon));

        Assert.Equal(new SyncPosition(Noon, null), await GetPositionAsync());
    }

    [Fact]
    public async Task ALaterRun_MovesTheWatermarkForward()
    {
        await CompleteRunAsync(await StartRunAsync(), Outcome(watermarkThrough: Noon));

        await CompleteRunAsync(
            await StartRunAsync(),
            Outcome(watermarkThrough: Noon.AddMinutes(30))
        );

        Assert.Equal(Noon.AddMinutes(30), (await GetPositionAsync()).SourceUpdatedThrough);
    }

    // Sync holds the watermark back per run against what did not resolve. The
    // store must not undo that by sliding it forward on its own, and must not
    // slide it back either.
    [Fact]
    public async Task AnOlderWatermark_DoesNotPushTheStoredOneBackwards()
    {
        await CompleteRunAsync(await StartRunAsync(), Outcome(watermarkThrough: Noon));

        await CompleteRunAsync(
            await StartRunAsync(),
            Outcome(watermarkThrough: Noon.AddMinutes(-30))
        );

        Assert.Equal(Noon, (await GetPositionAsync()).SourceUpdatedThrough);
    }

    [Fact]
    public async Task ARunThatReadNothing_LeavesTheWatermarkWhereItWas()
    {
        await CompleteRunAsync(await StartRunAsync(), Outcome(watermarkThrough: Noon));

        await CompleteRunAsync(await StartRunAsync(), Outcome());

        Assert.Equal(Noon, (await GetPositionAsync()).SourceUpdatedThrough);
    }

    // A run the page cap cut short leaves the token and holds the watermark, so
    // the next run resumes mid-batch instead of restarting at the top of it.
    [Fact]
    public async Task ATruncatedRun_StoresTheTokenAndLeavesTheWatermark()
    {
        await CompleteRunAsync(await StartRunAsync(), Outcome(watermarkThrough: Noon));

        var runId = await StartRunAsync();
        await CompleteRunAsync(
            runId,
            Outcome(watermarkThrough: Noon, continuationToken: "token-7", truncated: true)
        );

        Assert.Equal(new SyncPosition(Noon, "token-7"), await GetPositionAsync());

        using var context = _factory.CreateContext();
        Assert.True(
            await context.SyncRuns.Where(r => r.Id == runId).Select(r => r.Truncated).SingleAsync()
        );
    }

    // The token skips the next run past what this one could not place.
    [Fact]
    public async Task ATruncatedRun_StoresTheHoldbackAlongsideItsToken()
    {
        await CompleteRunAsync(
            await StartRunAsync(),
            Outcome(
                watermarkThrough: Noon,
                continuationToken: "token-7",
                unresolvedFrom: Noon.AddMinutes(10),
                truncated: true
            )
        );

        Assert.Equal(
            new SyncPosition(Noon, "token-7", Noon.AddMinutes(10)),
            await GetPositionAsync()
        );
    }

    [Fact]
    public async Task ARunThatDrained_ClearsAHoldbackLeftByTheOneBeforeIt()
    {
        await CompleteRunAsync(
            await StartRunAsync(),
            Outcome(
                watermarkThrough: Noon,
                continuationToken: "token-7",
                unresolvedFrom: Noon.AddMinutes(10),
                truncated: true
            )
        );

        await CompleteRunAsync(
            await StartRunAsync(),
            Outcome(watermarkThrough: Noon.AddMinutes(10))
        );

        Assert.Null((await GetPositionAsync()).UnresolvedFrom);
    }

    // The forward-only guard ignores a null watermark, so a position with
    // nothing to move it to still has to land its cleared token.
    [Fact]
    public async Task APositionWithNoWatermark_StillClearsTheToken()
    {
        await CompleteRunAsync(
            await StartRunAsync(),
            Outcome(watermarkThrough: Noon, continuationToken: "token-7", truncated: true)
        );

        await CompleteRunAsync(
            await StartRunAsync(),
            new VisitSyncOutcome(new VisitIngestionResult(0, 0, 0), 0, new SyncPosition(null, null))
        );

        Assert.Equal(new SyncPosition(Noon, null), await GetPositionAsync());
    }

    [Fact]
    public async Task ARunThatDrained_ClearsATokenLeftByTheOneBeforeIt()
    {
        await CompleteRunAsync(
            await StartRunAsync(),
            Outcome(watermarkThrough: Noon, continuationToken: "token-7", truncated: true)
        );

        await CompleteRunAsync(
            await StartRunAsync(),
            Outcome(watermarkThrough: Noon.AddMinutes(30))
        );

        Assert.Equal(new SyncPosition(Noon.AddMinutes(30), null), await GetPositionAsync());
    }

    // The watermark only moves on a run that finished, so the next tick
    // refetches exactly what this one failed on.
    [Fact]
    public async Task AFailedRun_LeavesTheWatermarkWhereItWas()
    {
        await CompleteRunAsync(await StartRunAsync(), Outcome(watermarkThrough: Noon));
        var runId = await StartRunAsync();

        using (var context = _factory.CreateContext())
        {
            await StoreOn(context)
                .FailRunAsync(runId, "the source is down", CancellationToken.None);
        }

        Assert.Equal(Noon, (await GetPositionAsync()).SourceUpdatedThrough);

        using var assertContext = _factory.CreateContext();
        var run = await assertContext.SyncRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(SyncRunStatus.Failed, run.Status);
        Assert.Equal("the source is down", run.Error);
        Assert.NotNull(run.CompletedAt);
    }

    // The row is the only record of what went wrong, so an error too long for
    // the column must not take it down with it. Kept from the front, where the
    // exception type and message are.
    [Fact]
    public async Task AnErrorLongerThanTheColumn_KeepsItsBeginning()
    {
        var runId = await StartRunAsync();

        using (var context = _factory.CreateContext())
        {
            await StoreOn(context)
                .FailRunAsync(
                    runId,
                    "HttpRequestException: " + new string('x', 5000) + "TAIL",
                    CancellationToken.None
                );
        }

        using var assertContext = _factory.CreateContext();
        var run = await assertContext.SyncRuns.SingleAsync(r => r.Id == runId);

        Assert.Equal(2000, run.Error?.Length);
        Assert.StartsWith("HttpRequestException: ", run.Error, StringComparison.Ordinal);
    }

    // Without both halves of the key in the query, a vedtak watermark would be
    // handed to the visit sync and advance it past visits nobody read.
    [Fact]
    public async Task AWatermarkForAnotherResourceType_IsNotHandedToThisOne()
    {
        await CompleteRunAsync(
            await StartRunAsync(ASecondResourceType),
            Outcome(watermarkThrough: Noon)
        );

        Assert.Equal(SyncPosition.Start, await GetPositionAsync());
        Assert.Equal(Noon, (await GetPositionAsync(ASecondResourceType)).SourceUpdatedThrough);
    }

    [Fact]
    public async Task CompletingARunThatDoesNotExist_Throws()
    {
        using var context = _factory.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StoreOn(context).CompleteRunAsync(runId: 12345, Outcome(), CancellationToken.None)
        );
    }
}
