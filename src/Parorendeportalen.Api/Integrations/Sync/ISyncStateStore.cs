namespace Parorendeportalen.Api.Integrations.Sync;

public interface ISyncStateStore
{
    Task<SyncPosition> GetPositionAsync(
        SourceSystem sourceSystem,
        SyncResourceType resourceType,
        CancellationToken cancellationToken
    );

    Task<int> StartRunAsync(
        SourceSystem sourceSystem,
        SyncResourceType resourceType,
        CancellationToken cancellationToken
    );

    // The position moves here and nowhere else, so a run that did not finish
    // cannot leave the watermark past data nobody read.
    Task CompleteRunAsync(int runId, VisitSyncOutcome outcome, CancellationToken cancellationToken);

    Task FailRunAsync(int runId, string error, CancellationToken cancellationToken);
}
