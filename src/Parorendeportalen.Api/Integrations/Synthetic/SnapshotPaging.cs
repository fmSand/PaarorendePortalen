namespace Parorendeportalen.Api.Integrations.Synthetic;

// Paging for a source that holds its snapshots in memory. Split out so tests can
// drive the cursor logic with hand-built snapshots.
public static class SnapshotPaging
{
    public static VisitSnapshotPage Page(
        IEnumerable<VisitSnapshot> snapshots,
        VisitSourceCursor cursor,
        int pageSize
    )
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var position = SyntheticPagePosition.Parse(cursor.ContinuationToken);

        var matching = snapshots
            .OrderBy(snapshot => snapshot.SourceUpdatedAt)
            .ThenBy(snapshot => snapshot.ExternalId, StringComparer.Ordinal)
            .Where(snapshot =>
                (cursor.ChangedSince is null || snapshot.SourceUpdatedAt >= cursor.ChangedSince)
                && (position is null || position.Value.Precedes(snapshot))
            );

        // One past the page, so a full page that ends on the last snapshot has HasMore false.
        var page = matching.Take(pageSize + 1).ToList();
        var hasMore = page.Count > pageSize;
        if (hasMore)
        {
            page.RemoveAt(pageSize);
        }

        var continuationToken = hasMore ? SyntheticPagePosition.From(page[^1]).ToToken() : null;

        return new VisitSnapshotPage(page, continuationToken);
    }
}
