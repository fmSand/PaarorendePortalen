namespace Parorendeportalen.Api.Integrations;

public sealed record VisitSnapshotPage(
    IReadOnlyList<VisitSnapshot> Snapshots,
    string? ContinuationToken)
{
    // A page can filter down to nothing and still have pages behind it.
    public bool HasMore => ContinuationToken is not null;
}
