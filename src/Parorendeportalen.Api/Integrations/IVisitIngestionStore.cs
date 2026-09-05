using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Integrations;

// Kept off IVisitRepository, which stays the query contract the controller uses.
public interface IVisitIngestionStore
{
    // Idempotent on (Origin, ExternalId). Incremental also leaves a ChangeEvent per insert and update, saved together with the visit.
    Task<VisitIngestionResult> UpsertAsync(
        IReadOnlyList<Visit> visits,
        IngestionMode mode,
        CancellationToken cancellationToken
    );
}
