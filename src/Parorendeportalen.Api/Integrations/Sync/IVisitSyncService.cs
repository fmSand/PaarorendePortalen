namespace Parorendeportalen.Api.Integrations.Sync;

// Takes the source rather than depending on one, so a second source is a
// second worker registration instead of a branch in here.
public interface IVisitSyncService
{
    Task<VisitSyncOutcome> RunAsync(
        IVisitSource source,
        SyncPosition resumeFrom,
        CancellationToken cancellationToken
    );
}
