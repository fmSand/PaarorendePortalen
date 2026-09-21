using Parorendeportalen.Api.Models.Visits;

namespace Parorendeportalen.Api.Integrations;

// Portal rows are written through IVisitRepository.
public interface IVisitIngestionStore
{
    // Idempotent on (Origin, ExternalId). Incremental also leaves a ChangeEvent per insert and update, saved together with the visit.
    Task<VisitIngestionResult> UpsertAsync(
        IReadOnlyList<Visit> visits,
        IngestionMode mode,
        CancellationToken cancellationToken
    );
}
