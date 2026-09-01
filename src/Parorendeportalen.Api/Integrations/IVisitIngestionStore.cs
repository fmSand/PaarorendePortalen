using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Integrations;

// Kept off IVisitRepository, which stays the query contract the controller uses.
public interface IVisitIngestionStore
{
    // Idempotent on (Origin, ExternalId).
    Task<VisitIngestionResult> UpsertAsync(
        IReadOnlyList<Visit> visits,
        CancellationToken cancellationToken
    );
}
