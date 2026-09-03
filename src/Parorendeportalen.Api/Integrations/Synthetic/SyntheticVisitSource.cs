namespace Parorendeportalen.Api.Integrations.Synthetic;

// Stands in for a municipal EPJ. The gates on those are institutional rather
// than technical, so this is the implementation that ships.
public sealed class SyntheticVisitSource : IVisitSource
{
    public const int DefaultPageSize = 50;

    private readonly IReadOnlyList<SyntheticRecipient> _careRecipients;
    private readonly TimeProvider _timeProvider;
    private readonly int _pageSize;

    public SyntheticVisitSource(
        IReadOnlyList<SyntheticRecipient> careRecipients,
        TimeProvider timeProvider,
        int pageSize = DefaultPageSize
    )
    {
        ArgumentNullException.ThrowIfNull(careRecipients);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        _careRecipients = careRecipients;
        _timeProvider = timeProvider;
        _pageSize = pageSize;
    }

    public SourceSystem SourceSystem => SourceSystem.Synthetic;

    public Task<VisitSnapshotPage> FetchVisitsChangedSinceAsync(
        VisitSourceCursor cursor,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(cursor);
        cancellationToken.ThrowIfCancellationRequested();

        // Built per fetch, so a visit that has been and gone stops reporting
        // itself as planned. Paging can only skip a snapshot that moves
        // earlier, and the feed keeps every publication time below where a
        // finished visit lands.
        var snapshots = SyntheticVisitFeed.Build(_careRecipients, _timeProvider.GetUtcNow());

        return Task.FromResult(SnapshotPaging.Page(snapshots, cursor, _pageSize));
    }
}
