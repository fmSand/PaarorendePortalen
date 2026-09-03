using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Synthetic;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Integrations.Synthetic;

public class SnapshotPagingTests
{
    private const int PageLimit = 20;

    private static readonly DateTimeOffset Noon = Snapshots.Noon;

    [Fact]
    public void TheSameCursorTwice_ReturnsTheSamePage()
    {
        VisitSnapshot[] snapshots =
        [
            Snapshots.Visit("visit-0002", Noon.AddMinutes(2)),
            Snapshots.Visit("visit-0001", Noon.AddMinutes(1)),
        ];

        var first = SnapshotPaging.Page(snapshots, VisitSourceCursor.Initial, pageSize: 10);
        var second = SnapshotPaging.Page(snapshots, VisitSourceCursor.Initial, pageSize: 10);

        Assert.Equal(2, first.Snapshots.Count);
        Assert.Equal(first.Snapshots, second.Snapshots);
        Assert.Equal(first.ContinuationToken, second.ContinuationToken);
    }

    [Fact]
    public void APage_IsOrderedBySourceUpdatedAt_ThenExternalId()
    {
        VisitSnapshot[] snapshots =
        [
            Snapshots.Visit("visit-c", Noon.AddMinutes(1)),
            Snapshots.Visit("visit-a", Noon.AddMinutes(2)),
            Snapshots.Visit("visit-b", Noon.AddMinutes(1)),
        ];

        var page = SnapshotPaging.Page(snapshots, VisitSourceCursor.Initial, pageSize: 10);

        Assert.Equal(
            ["visit-b", "visit-c", "visit-a"],
            page.Snapshots.Select(snapshot => snapshot.ExternalId)
        );
    }

    // VisitSourceCursor exists: '>' would drop the tie and '>='
    // alone would never get past it.
    [Fact]
    public void PagingWalksPast_ARunOfSnapshotsSharingOneSourceUpdatedAt()
    {
        var published = Noon.AddMinutes(5);
        VisitSnapshot[] snapshots =
        [
            Snapshots.Visit("visit-0001", published),
            Snapshots.Visit("visit-0002", published),
            Snapshots.Visit("visit-0003", published),
        ];

        Assert.Equal(
            ["visit-0001", "visit-0002", "visit-0003"],
            WalkEveryPage(snapshots, VisitSourceCursor.Since(published), pageSize: 2)
        );
    }

    [Fact]
    public void AFullPage_ReportsNoMore_WhenNothingIsBehindIt()
    {
        VisitSnapshot[] snapshots =
        [
            Snapshots.Visit("visit-0001", Noon.AddMinutes(1)),
            Snapshots.Visit("visit-0002", Noon.AddMinutes(2)),
        ];

        var page = SnapshotPaging.Page(snapshots, VisitSourceCursor.Initial, pageSize: 2);

        Assert.Equal(2, page.Snapshots.Count);
        Assert.False(page.HasMore);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public void AFullPage_ReportsMore_WhenASnapshotRemainsBehindIt()
    {
        VisitSnapshot[] snapshots =
        [
            Snapshots.Visit("visit-0001", Noon.AddMinutes(1)),
            Snapshots.Visit("visit-0002", Noon.AddMinutes(2)),
            Snapshots.Visit("visit-0003", Noon.AddMinutes(3)),
        ];

        var page = SnapshotPaging.Page(snapshots, VisitSourceCursor.Initial, pageSize: 2);

        Assert.Equal(2, page.Snapshots.Count);
        Assert.True(page.HasMore);
        Assert.NotNull(page.ContinuationToken);
    }

    // The token has to carry the end of the page. Issuing the first entry's
    // position instead would re-serve everything after it on the next page.
    [Fact]
    public void EveryPage_IsServedOnce_AcrossAWholeWalk()
    {
        VisitSnapshot[] snapshots =
        [
            .. Enumerable
                .Range(1, 7)
                .Select(n => Snapshots.Visit($"visit-{n:D4}", Noon.AddMinutes(n))),
        ];

        var seen = WalkEveryPage(snapshots, VisitSourceCursor.Initial, pageSize: 2);

        Assert.Equal(7, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct(StringComparer.Ordinal).Count());
    }

    // Inclusive, because the watermark is stored as the newest instant seen and
    // a second visit can share it.
    [Fact]
    public void ChangedSince_IncludesASnapshotOnTheBoundary()
    {
        var boundary = Noon.AddMinutes(5);
        VisitSnapshot[] snapshots =
        [
            Snapshots.Visit("visit-older", boundary.AddTicks(-1)),
            Snapshots.Visit("visit-boundary", boundary),
        ];

        var page = SnapshotPaging.Page(snapshots, VisitSourceCursor.Since(boundary), pageSize: 10);

        Assert.Equal(["visit-boundary"], page.Snapshots.Select(snapshot => snapshot.ExternalId));
    }

    // The token outlives the process in SyncWatermarks, so failing on one this
    // source can no longer read would fail every tick until someone cleared it.
    [Fact]
    public void ATokenFromSomewhereElse_ReadsFromTheStartOfTheWindow()
    {
        var page = SnapshotPaging.Page(
            [Snapshots.Visit("visit-0001", Noon)],
            VisitSourceCursor.Since(Noon).Next("not-a-token"),
            pageSize: 10
        );

        Assert.Equal(["visit-0001"], page.Snapshots.Select(snapshot => snapshot.ExternalId));
    }

    [Fact]
    public void NoSnapshots_MeansAnEmptyPageWithNothingBehindIt()
    {
        var page = SnapshotPaging.Page([], VisitSourceCursor.Initial, pageSize: 10);

        Assert.Empty(page.Snapshots);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void APageSizeBelowOne_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SnapshotPaging.Page([], VisitSourceCursor.Initial, pageSize: 0)
        );
    }

    // Capped, so a paging bug that stops advancing fails here instead of
    // hanging the run until CI times out with nothing to read.
    private static List<string> WalkEveryPage(
        IReadOnlyList<VisitSnapshot> snapshots,
        VisitSourceCursor start,
        int pageSize
    )
    {
        var seen = new List<string>();
        var cursor = start;

        for (var page = 0; page < PageLimit; page++)
        {
            var fetched = SnapshotPaging.Page(snapshots, cursor, pageSize);
            seen.AddRange(fetched.Snapshots.Select(snapshot => snapshot.ExternalId));

            if (!fetched.HasMore)
            {
                return seen;
            }

            cursor = cursor.Next(fetched.ContinuationToken!);
        }

        Assert.Fail($"Paging did not finish within {PageLimit} pages; it is not advancing.");
        return seen;
    }
}
