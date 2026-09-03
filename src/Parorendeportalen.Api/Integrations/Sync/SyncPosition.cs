namespace Parorendeportalen.Api.Integrations.Sync;

// Where the next run picks up. The token is set only when a run stopped at the
// page cap, and a run that drained clears it. UnresolvedFrom follows the token.
public sealed record SyncPosition(
    DateTimeOffset? SourceUpdatedThrough,
    string? ContinuationToken,
    DateTimeOffset? UnresolvedFrom = null
)
{
    public static SyncPosition Start { get; } = new(null, null);

    public VisitSourceCursor ToCursor()
    {
        var cursor = SourceUpdatedThrough is { } changedSince
            ? VisitSourceCursor.Since(changedSince)
            : VisitSourceCursor.Initial;

        return ContinuationToken is null ? cursor : cursor.Next(ContinuationToken);
    }
}
