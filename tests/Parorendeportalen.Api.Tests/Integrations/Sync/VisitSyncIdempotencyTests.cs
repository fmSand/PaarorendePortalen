using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Sync;
using Parorendeportalen.Api.Integrations.Synthetic;
using Parorendeportalen.Api.Models.Kinship;
using Parorendeportalen.Api.Models.Visits;
using Parorendeportalen.Api.Repositories.Kinship;
using Parorendeportalen.Api.Services.Kinship;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Integrations.Sync;

// The whole loop against Postgres: a scripted source, the EF ingestion store and the stored
// watermark. Only at this level can a second run over the same data be shown to be a no-op.
[Collection(PostgresCollection.Name)]
public class VisitSyncIdempotencyTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = Snapshots.Noon;

    private readonly NationalIdHasher _hasher = new("test-pepper");
    private PostgresTestDatabase _factory = null!;
    private int _vigdisId;

    public async Task InitializeAsync()
    {
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

        using var context = _factory.CreateContext();
        var vigdis = new CareRecipient
        {
            Name = "Vigdis Quist",
            NationalIdHash = _hasher.Hash(Snapshots.Vigdis.HashInput),
        };
        context.CareRecipients.Add(vigdis);
        await context.SaveChangesAsync();

        _vigdisId = vigdis.Id;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private VisitSyncService SyncOn(AppDbContext context) =>
        new(
            new EfVisitIngestionStore(context, new FixedTimeProvider(Noon)),
            new EfCareRecipientRepository(context),
            _hasher,
            NullLogger<VisitSyncService>.Instance
        );

    private async Task<VisitSyncOutcome> RunAsync(IVisitSource source, SyncPosition? resumeFrom)
    {
        using var context = _factory.CreateContext();

        return await SyncOn(context)
            .RunAsync(source, resumeFrom ?? SyncPosition.Start, CancellationToken.None);
    }

    private static VisitSnapshot Planned(string externalId, DateTimeOffset sourceUpdatedAt) =>
        Snapshots.Visit(externalId, sourceUpdatedAt, scheduledAt: sourceUpdatedAt.AddHours(2));

    // Same page twice, then a changed row, then a failure (NSubstitute can only script an interface)
    [Fact]
    public async Task AScriptedSource_InsertsThenReportsUnchangedThenUpdatesThenFails()
    {
        var first = Planned("visit-0001", Noon);
        var second = Planned("visit-0002", Noon.AddMinutes(1));
        var changed = second with
        {
            SourceUpdatedAt = Noon.AddMinutes(10),
            Status = VisitStatus.Completed,
            ActualAt = Noon.AddHours(2).AddMinutes(4),
        };

        var source = new ScriptedVisitSource(
            () => ScriptedVisitSource.LastPage(first, second),
            () => ScriptedVisitSource.LastPage(first, second),
            () => ScriptedVisitSource.LastPage(changed),
            () => throw new HttpRequestException("the source is down")
        );

        var insert = await RunAsync(source, SyncPosition.Start);
        Assert.Equal(new VisitIngestionResult(2, 0, 0), insert.Ingestion);
        Assert.Equal(Noon.AddMinutes(1), insert.Position!.SourceUpdatedThrough);

        var rerun = await RunAsync(source, insert.Position);
        Assert.Equal(new VisitIngestionResult(0, 0, 2), rerun.Ingestion);

        var update = await RunAsync(source, rerun.Position);
        Assert.Equal(new VisitIngestionResult(0, 1, 0), update.Ingestion);
        Assert.Equal(Noon.AddMinutes(10), update.Position!.SourceUpdatedThrough);

        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync(source, update.Position));

        using var context = _factory.CreateContext();
        Assert.Equal(2, await context.Visits.CountAsync());
        var completed = await context.Visits.SingleAsync(v => v.ExternalId == "visit-0002");
        Assert.Equal(VisitStatus.Completed, completed.Status);
        Assert.Equal(Noon.AddHours(2).AddMinutes(4), completed.ActualAt);
    }

    [Fact]
    public async Task TheRerun_AsksTheSourceForChangesSinceTheWatermarkTheFirstRunLeft()
    {
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(Planned("visit-0001", Noon))
        );

        var first = await RunAsync(source, SyncPosition.Start);
        await RunAsync(source, first.Position);

        Assert.Equal([null, Noon], source.Cursors.Select(cursor => cursor.ChangedSince));
    }

    // The adapter that ships, paged small enough that the run has to follow the
    // continuation token through the tie its planned visits share.
    [Fact]
    public async Task TheSyntheticAdapter_AddsNothingOnASecondRunOverTheSameFeed()
    {
        var source = new SyntheticVisitSource(
            [Snapshots.VigdisRecipient],
            new FixedTimeProvider(Noon),
            pageSize: 5
        );

        var first = await RunAsync(source, SyncPosition.Start);

        Assert.True(first.Ingestion.Inserted > 5, "the feed should span several pages");
        Assert.Equal(0, first.Ingestion.Updated);
        Assert.Equal(0, first.Ingestion.Unchanged);

        // From the top, so every row is compared again through the whole paging path.
        // (A watermark-driven rerun only re-reads the boundary and would miss a field the store fails to write.)
        var whole = await RunAsync(source, SyncPosition.Start);

        Assert.Equal(new VisitIngestionResult(0, 0, first.Ingestion.Inserted), whole.Ingestion);

        var fromWatermark = await RunAsync(source, first.Position);

        Assert.Equal(0, fromWatermark.Ingestion.Inserted);
        Assert.Equal(0, fromWatermark.Ingestion.Updated);

        using var context = _factory.CreateContext();
        Assert.Equal(first.Ingestion.Inserted, await context.Visits.CountAsync());
        Assert.All(
            await context.Visits.ToListAsync(),
            visit =>
            {
                Assert.Equal(Origin.Synthetic, visit.Origin);
                Assert.Equal(_vigdisId, visit.CareRecipientId);
            }
        );
    }

    [Fact]
    public async Task TheSyntheticAdapter_ReportsAVisitAsUpdated_OnceItHasHappened()
    {
        var clock = new FixedTimeProvider(Snapshots.Midnight.AddHours(7));
        var source = new SyntheticVisitSource([Snapshots.VigdisRecipient], clock);

        var first = await RunAsync(source, SyncPosition.Start);
        Assert.True(first.Ingestion.Inserted > 0);

        clock.Now = Snapshots.Midnight.AddHours(9);

        var afterTheVisit = await RunAsync(source, first.Position);

        Assert.Equal(0, afterTheVisit.Ingestion.Inserted);
        Assert.Equal(1, afterTheVisit.Ingestion.Updated);

        using var context = _factory.CreateContext();
        var morning = await context.Visits.SingleAsync(v =>
            v.ScheduledAt == Snapshots.Midnight.AddHours(8)
        );
        Assert.NotEqual(VisitStatus.Planned, morning.Status);
    }

    [Fact]
    public async Task ASnapshotForAnUnknownRecipient_HoldsTheWatermarkBackUntilSheIsSeeded()
    {
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(
                Snapshots.Visit("visit-0001", Noon, Snapshots.Vigdis),
                Snapshots.Visit("visit-0002", Noon.AddMinutes(5), Snapshots.Tor)
            )
        );

        var held = await RunAsync(source, SyncPosition.Start);

        Assert.Equal(1, held.UnresolvedSnapshots);
        Assert.Equal(Noon.AddMinutes(5), held.Position!.SourceUpdatedThrough);

        using (var context = _factory.CreateContext())
        {
            context.CareRecipients.Add(
                new CareRecipient
                {
                    Name = "Tor Quist",
                    NationalIdHash = _hasher.Hash(Snapshots.Tor.HashInput),
                }
            );
            await context.SaveChangesAsync();
        }

        var caughtUp = await RunAsync(source, held.Position);

        Assert.Equal(0, caughtUp.UnresolvedSnapshots);
        Assert.Equal(1, caughtUp.Ingestion.Inserted);

        using var assertContext = _factory.CreateContext();
        Assert.Equal(2, await assertContext.Visits.CountAsync());
    }

    // A source is free to send Oslo local time. Postgres holds instants, so the visit
    // and the watermark the run leaves behind both have to arrive as UTC.
    [Fact]
    public async Task ASourceSendingOsloLocalTime_StoresTheInstant_AndAWatermarkOfItsOwn()
    {
        var osloMorning = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(2));
        var planned = Planned("visit-0001", osloMorning);
        var completed = planned with
        {
            SourceUpdatedAt = osloMorning.AddHours(3),
            Status = VisitStatus.Completed,
            ActualAt = osloMorning.AddHours(2).AddMinutes(6),
        };
        var source = new ScriptedVisitSource(
            () => ScriptedVisitSource.LastPage(planned),
            () => ScriptedVisitSource.LastPage(planned),
            () => ScriptedVisitSource.LastPage(completed)
        );

        var run = await RunAsync(source, SyncPosition.Start);
        Assert.Equal(new VisitIngestionResult(1, 0, 0), run.Ingestion);

        // The same page again: an offset left on the stored value would read back as a change.
        var rerun = await RunAsync(source, run.Position);
        Assert.Equal(new VisitIngestionResult(0, 0, 1), rerun.Ingestion);

        var settled = await RunAsync(source, rerun.Position);
        Assert.Equal(new VisitIngestionResult(0, 1, 0), settled.Ingestion);

        using var context = _factory.CreateContext();
        var state = new EfSyncStateStore(context, new FixedTimeProvider(Noon));
        var runId = await state.StartRunAsync(
            SourceSystem.Synthetic,
            SyncResourceType.Visit,
            CancellationToken.None
        );
        await state.CompleteRunAsync(runId, settled, CancellationToken.None);

        var stored = await context.Visits.SingleAsync();
        Assert.Equal(osloMorning.AddHours(2), stored.ScheduledAt);
        Assert.Equal(TimeSpan.Zero, stored.ScheduledAt.Offset);
        Assert.Equal(osloMorning.AddHours(2).AddMinutes(6), stored.ActualAt);
        Assert.Equal(TimeSpan.Zero, stored.ActualAt!.Value.Offset);

        var watermark = await context.SyncWatermarks.SingleAsync();
        Assert.Equal(osloMorning.AddHours(3), watermark.SourceUpdatedThrough);
        Assert.Equal(TimeSpan.Zero, watermark.SourceUpdatedThrough!.Value.Offset);
    }
}
