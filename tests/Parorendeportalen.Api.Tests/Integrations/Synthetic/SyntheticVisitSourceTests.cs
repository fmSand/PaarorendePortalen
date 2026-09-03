using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Synthetic;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Integrations.Synthetic;

public class SyntheticVisitSourceTests
{
    private static readonly DateTimeOffset Noon = Snapshots.Noon;

    private static SyntheticVisitSource SourceAt(
        FixedTimeProvider clock,
        int pageSize = SyntheticVisitSource.DefaultPageSize
    ) => new([Snapshots.VigdisRecipient], clock, pageSize);

    private static async Task<IReadOnlyList<VisitSnapshot>> AllSnapshotsAsync(
        SyntheticVisitSource source
    )
    {
        var all = new List<VisitSnapshot>();
        var cursor = VisitSourceCursor.Initial;

        for (var page = 0; page < 50; page++)
        {
            var fetched = await source.FetchVisitsChangedSinceAsync(cursor, CancellationToken.None);
            all.AddRange(fetched.Snapshots);

            if (!fetched.HasMore)
            {
                return all;
            }

            cursor = cursor.Next(fetched.ContinuationToken!);
        }

        Assert.Fail("The source never stopped issuing continuation tokens.");
        return all;
    }

    [Fact]
    public void SourceSystem_NamesTheSystemTheSnapshotsCarry()
    {
        Assert.Equal(SourceSystem.Synthetic, SourceAt(new FixedTimeProvider(Noon)).SourceSystem);
    }

    [Fact]
    public async Task AVisitThatHasHappened_StopsBeingPlanned_OnceTheClockPassesIt()
    {
        var clock = new FixedTimeProvider(Snapshots.Midnight.AddHours(7));
        var source = SourceAt(clock);

        var beforeTheVisit = await AllSnapshotsAsync(source);
        var morning = Assert.Single(
            beforeTheVisit,
            snapshot => snapshot.ScheduledAt == Snapshots.Midnight.AddHours(8)
        );
        Assert.Equal(VisitStatus.Planned, morning.Status);

        clock.Now = Snapshots.Midnight.AddHours(9);

        var afterTheVisit = await AllSnapshotsAsync(source);
        var same = Assert.Single(
            afterTheVisit,
            snapshot => snapshot.ExternalId == morning.ExternalId
        );

        Assert.NotEqual(VisitStatus.Planned, same.Status);
        Assert.Equal(morning.ScheduledAt, same.ScheduledAt);
    }

    [Fact]
    public async Task FinishingAVisit_MovesItLaterInTheOrder_NeverEarlier()
    {
        var clock = new FixedTimeProvider(Snapshots.Midnight.AddHours(7));
        var source = SourceAt(clock);

        var planned = Assert.Single(
            await AllSnapshotsAsync(source),
            snapshot => snapshot.ScheduledAt == Snapshots.Midnight.AddHours(8)
        );

        clock.Now = Snapshots.Midnight.AddHours(9);

        var finished = Assert.Single(
            await AllSnapshotsAsync(source),
            snapshot => snapshot.ExternalId == planned.ExternalId
        );

        Assert.True(finished.SourceUpdatedAt > planned.SourceUpdatedAt);
    }

    [Fact]
    public async Task TheSourceServesEveryVisitInItsWindow_AcrossPages()
    {
        var source = SourceAt(new FixedTimeProvider(Noon), pageSize: 4);

        var all = await AllSnapshotsAsync(source);

        Assert.Equal(
            all.Count,
            all.Select(snapshot => snapshot.ExternalId).Distinct(StringComparer.Ordinal).Count()
        );
        Assert.True(all.Count > 4, "the window should span more than a single page");
    }

    [Fact]
    public async Task NoCareRecipients_MeansNothingToServe()
    {
        var source = new SyntheticVisitSource([], new FixedTimeProvider(Noon));

        var page = await source.FetchVisitsChangedSinceAsync(
            VisitSourceCursor.Initial,
            CancellationToken.None
        );

        Assert.Empty(page.Snapshots);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void APageSizeBelowOne_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SyntheticVisitSource([], new FixedTimeProvider(Noon), pageSize: 0)
        );
    }

    [Fact]
    public async Task ACancelledFetch_Throws_BeforeReturningAPage()
    {
        var source = SourceAt(new FixedTimeProvider(Noon));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            source.FetchVisitsChangedSinceAsync(VisitSourceCursor.Initial, cancellation.Token)
        );
    }
}
