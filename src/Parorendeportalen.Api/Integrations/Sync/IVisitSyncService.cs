namespace Parorendeportalen.Api.Integrations.Sync;

// Takes the source as a parameter, so a second source is a second worker
// registration.
public interface IVisitSyncService
{
    Task<VisitSyncOutcome> RunAsync(
        IVisitSource source,
        SyncPosition resumeFrom,
        CancellationToken cancellationToken
    );
}
