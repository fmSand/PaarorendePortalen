using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Sync;
using Parorendeportalen.Api.Integrations.Synthetic;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Integrations.Sync;

[Collection(PostgresCollection.Name)]
public class VisitSyncWorkerTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = Snapshots.Noon;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly NationalIdHasher _hasher = new("test-pepper");
    private readonly CapturingLogger<VisitSyncWorker> _logger = new();
    private PostgresTestDatabase _factory = null!;
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

        using (var context = _factory.CreateContext())
        {
            context.CareRecipients.Add(
                new CareRecipient
                {
                    Name = "Vigdis Quist",
                    NationalIdHash = _hasher.Hash(Snapshots.Vigdis.HashInput),
                }
            );
            await context.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(_factory.ConnectionString)
        );
        services.AddSingleton(_hasher);
        services.AddScoped<ICareRecipientRepository, EfCareRecipientRepository>();
        services.AddScoped<IVisitIngestionStore, EfVisitIngestionStore>();
        services.AddScoped<ISyncStateStore, EfSyncStateStore>();
        services.AddScoped<IVisitSyncService, VisitSyncService>();
        services.AddSingleton(TimeProvider.System);

        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _factory.DisposeAsync();
    }

    private VisitSyncWorker WorkerOver(IVisitSource source) =>
        new(
            source,
            _services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            new VisitSyncOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(200),
                MaxBackoffMultiplier = 1,
            },
            _logger
        );

    private async Task<IReadOnlyList<SyncRun>> WaitForFinishedRunsAsync(int count)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            using var context = _factory.CreateContext();
            var runs = await context
                .SyncRuns.AsNoTracking()
                .Where(r => r.Status != SyncRunStatus.Running)
                .OrderBy(r => r.Id)
                .ToListAsync();

            if (runs.Count >= count)
            {
                return runs;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException(
            $"Only saw fewer than {count} finished sync runs within {Timeout}. Errors logged: "
                + (_logger.Errors.Count == 0 ? "none" : string.Join(" | ", _logger.Errors))
        );
    }

    [Fact]
    public async Task AFirstTick_RunsTheSyncAndRecordsIt()
    {
        var worker = WorkerOver(
            new SyntheticVisitSource([Snapshots.VigdisRecipient], new FixedTimeProvider(Noon))
        );

        await worker.StartAsync(CancellationToken.None);
        var run = (await WaitForFinishedRunsAsync(1))[0];
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
        Assert.True(run.Inserted > 0);
        Assert.Null(run.Error);
        Assert.False(run.Truncated);

        using var context = _factory.CreateContext();
        Assert.Equal(run.Inserted, await context.Visits.CountAsync());
        Assert.NotNull(
            await context.SyncWatermarks.SingleAsync(w =>
                w.SourceSystem == SourceSystem.Synthetic && w.ResourceType == SyncResourceType.Visit
            )
        );
    }

    // A source that is down must not take the host down with it, and the reason
    // has to land somewhere a person can read it.
    [Fact]
    public async Task AFailingSource_LeavesAFailedRunBehind_AndTheWorkerKeepsTicking()
    {
        var worker = WorkerOver(
            new ScriptedVisitSource(() => throw new HttpRequestException("the source is down"))
        );

        await worker.StartAsync(CancellationToken.None);

        // A second finished run is what proves the loop carried on. Asserting
        // the task is merely unfinished would pass for a worker that hung.
        var runs = await WaitForFinishedRunsAsync(2);

        Assert.False(worker.ExecuteTask!.IsCompleted);
        await worker.StopAsync(CancellationToken.None);

        Assert.All(
            runs,
            run =>
            {
                Assert.Equal(SyncRunStatus.Failed, run.Status);
                Assert.Equal("the source is down", run.Error);
                Assert.NotNull(run.CompletedAt);
            }
        );

        using var context = _factory.CreateContext();
        Assert.Empty(context.SyncWatermarks);
    }

    // The watermark only moves on a run that finished, so a source that
    // recovers picks up exactly where the failures left it.
    [Fact]
    public async Task ASourceThatRecovers_SyncsOnTheNextTick()
    {
        var failures = 0;
        var source = new ScriptedVisitSource(() =>
            failures++ == 0
                ? throw new HttpRequestException("the source is down")
                : ScriptedVisitSource.LastPage(Snapshots.Visit("visit-0001", Noon))
        );

        var worker = WorkerOver(source);

        await worker.StartAsync(CancellationToken.None);
        var runs = await WaitForFinishedRunsAsync(2);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(SyncRunStatus.Failed, runs[0].Status);
        Assert.Equal(SyncRunStatus.Succeeded, runs[1].Status);
        Assert.Equal(1, runs[1].Inserted);

        using var context = _factory.CreateContext();
        Assert.Equal(Noon, (await context.SyncWatermarks.SingleAsync()).SourceUpdatedThrough);
    }

    // TaskCanceledException is an OperationCanceledException, so a filter on
    // the type alone lets a merely slow source escape the tick and stop the
    // host with the run left Running.
    [Fact]
    public async Task ASourceThatTimesOutOnItsOwn_IsRecordedLikeAnyOtherFailure()
    {
        var worker = WorkerOver(
            new ScriptedVisitSource(() => throw new TaskCanceledException("the source timed out"))
        );

        await worker.StartAsync(CancellationToken.None);
        var runs = await WaitForFinishedRunsAsync(2);

        Assert.False(worker.ExecuteTask!.IsCompleted);
        await worker.StopAsync(CancellationToken.None);

        Assert.All(
            runs,
            run =>
            {
                Assert.Equal(SyncRunStatus.Failed, run.Status);
                Assert.Equal("the source timed out", run.Error);
            }
        );
    }

    // The second run stores the token it resumed from, which is where draining
    // as fast as the source serves it turns into a busy loop.
    [Fact]
    public async Task ASourceStuckAtThePageCap_WaitsOutAPollInterval()
    {
        var source = new ScriptedVisitSource(() => new VisitSnapshotPage([], "always-more"));
        var worker = WorkerOver(source);

        await worker.StartAsync(CancellationToken.None);
        var runs = await WaitForFinishedRunsAsync(2);
        await worker.StopAsync(CancellationToken.None);

        Assert.All(runs, run => Assert.True(run.Truncated));
        Assert.Contains(
            _logger.Warnings,
            warning =>
                warning.Contains(
                    "without moving off the position it resumed from",
                    StringComparison.Ordinal
                )
        );
    }

    // The failure write runs on a token of its own. On the stopping token it
    // would be cancelled with the fetch and leave the row Running for good.
    [Fact]
    public async Task ARunStillFetchingWhenTheHostStops_IsStillRecordedAsFailed()
    {
        var source = new GatedVisitSource();
        var worker = WorkerOver(source);

        await worker.StartAsync(CancellationToken.None);
        await source.Entered.WaitAsync(Timeout);

        // Not awaited: StopAsync cancels before its first await, so the fetch
        // is released into a worker that is already stopping.
        var stopping = worker.StopAsync(CancellationToken.None);
        source.Release();
        await stopping;

        using var context = _factory.CreateContext();
        var run = await context.SyncRuns.SingleAsync();

        Assert.Equal(SyncRunStatus.Failed, run.Status);
        Assert.Equal("the source is down", run.Error);
        Assert.NotNull(run.CompletedAt);
    }
}
