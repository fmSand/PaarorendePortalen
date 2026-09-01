namespace Parorendeportalen.Api.Integrations;

// Unchanged is separate from Updated so a re-run is observably a no-op.
public sealed record VisitIngestionResult(int Inserted, int Updated, int Unchanged);
