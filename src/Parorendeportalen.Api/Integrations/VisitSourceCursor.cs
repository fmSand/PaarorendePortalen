namespace Parorendeportalen.Api.Integrations;

// A parameter object so a new cursor field never changes IVisitSource's signature.
public sealed record VisitSourceCursor(DateTimeOffset? ChangedSince, string? ContinuationToken)
{
    public static VisitSourceCursor Initial { get; } = new(null, null);

    public static VisitSourceCursor Since(DateTimeOffset changedSince) => new(changedSince, null);

    // Visits share a SourceUpdatedAt, so '>' drops the tie and '>=' never advances.
    public VisitSourceCursor Next(string continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(continuationToken);
        return this with { ContinuationToken = continuationToken };
    }
}
