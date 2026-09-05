namespace Parorendeportalen.Api.Integrations;

// No zero value, so a caller has to say which.
public enum IngestionMode
{
    // Imports old visits and records no change events, so nobody is notified.
    Backfill = 1,
    Incremental = 2,
}
