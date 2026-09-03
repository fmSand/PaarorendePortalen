using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;

namespace Parorendeportalen.Api.Integrations.Sync;

public sealed class EfSyncStateStore(AppDbContext context, TimeProvider timeProvider)
    : ISyncStateStore
{
    private const int ErrorMaxLength = 2000;

    public async Task<SyncPosition> GetPositionAsync(
        SourceSystem sourceSystem,
        SyncResourceType resourceType,
        CancellationToken cancellationToken
    )
    {
        var watermark = await context
            .SyncWatermarks.AsNoTracking()
            .FirstOrDefaultAsync(
                w => w.SourceSystem == sourceSystem && w.ResourceType == resourceType,
                cancellationToken
            );

        return watermark is null
            ? SyncPosition.Start
            : new SyncPosition(
                watermark.SourceUpdatedThrough,
                watermark.ContinuationToken,
                watermark.UnresolvedFrom
            );
    }

    public async Task<int> StartRunAsync(
        SourceSystem sourceSystem,
        SyncResourceType resourceType,
        CancellationToken cancellationToken
    )
    {
        var run = new SyncRun
        {
            SourceSystem = sourceSystem,
            ResourceType = resourceType,
            StartedAt = timeProvider.GetUtcNow(),
            Status = SyncRunStatus.Running,
        };

        context.SyncRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);

        return run.Id;
    }

    public async Task CompleteRunAsync(
        int runId,
        VisitSyncOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var run = await context.SyncRuns.FirstAsync(r => r.Id == runId, cancellationToken);

        run.Status = SyncRunStatus.Succeeded;
        run.CompletedAt = timeProvider.GetUtcNow();
        run.Inserted = outcome.Ingestion.Inserted;
        run.Updated = outcome.Ingestion.Updated;
        run.Unchanged = outcome.Ingestion.Unchanged;
        run.Unresolved = outcome.UnresolvedSnapshots;
        run.Truncated = outcome.Truncated;

        if (outcome.Position is { } position)
        {
            await SavePositionAsync(
                run.SourceSystem,
                run.ResourceType,
                position,
                cancellationToken
            );
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task FailRunAsync(int runId, string error, CancellationToken cancellationToken)
    {
        var run = await context.SyncRuns.FirstAsync(r => r.Id == runId, cancellationToken);

        run.Status = SyncRunStatus.Failed;
        run.CompletedAt = timeProvider.GetUtcNow();
        // Truncated rather than left to fail the insert, since this row is the
        // only record of what went wrong. Kept from the front, where the
        // exception type and message are.
        run.Error = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task SavePositionAsync(
        SourceSystem sourceSystem,
        SyncResourceType resourceType,
        SyncPosition position,
        CancellationToken cancellationToken
    )
    {
        var watermark = await context.SyncWatermarks.FirstOrDefaultAsync(
            w => w.SourceSystem == sourceSystem && w.ResourceType == resourceType,
            cancellationToken
        );

        if (watermark is null)
        {
            context.SyncWatermarks.Add(
                new SyncWatermark
                {
                    SourceSystem = sourceSystem,
                    ResourceType = resourceType,
                    SourceUpdatedThrough = position.SourceUpdatedThrough,
                    ContinuationToken = position.ContinuationToken,
                    UnresolvedFrom = position.UnresolvedFrom,
                }
            );

            return;
        }

        // Holding the watermark back is the sync service's decision, made per
        // run against what did not resolve. It never slides backwards on its
        // own. The token and the holdback are whatever the run left, both
        // cleared when it drained.
        if (
            watermark.SourceUpdatedThrough is null
            || position.SourceUpdatedThrough > watermark.SourceUpdatedThrough
        )
        {
            watermark.SourceUpdatedThrough = position.SourceUpdatedThrough;
        }

        watermark.ContinuationToken = position.ContinuationToken;
        watermark.UnresolvedFrom = position.UnresolvedFrom;
    }
}
