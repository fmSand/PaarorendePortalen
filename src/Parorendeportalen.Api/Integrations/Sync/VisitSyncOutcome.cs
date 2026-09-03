namespace Parorendeportalen.Api.Integrations.Sync;

// Position is null when nothing was read, which leaves the watermark where it
// was. Truncated says the page cap cut the run short, so the position carries a
// token and no advanced watermark.
public sealed record VisitSyncOutcome(
    VisitIngestionResult Ingestion,
    int UnresolvedSnapshots,
    SyncPosition? Position,
    bool Truncated = false
);
