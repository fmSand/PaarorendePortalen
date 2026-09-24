namespace Parorendeportalen.Api.Integrations;

// Unchanged is separate from Updated so a re-run is observably a no-op.
// Conflicted rows are left as stored.
public sealed record VisitIngestionResult(
    int Inserted,
    int Updated,
    int Unchanged,
    int Conflicted = 0
);
