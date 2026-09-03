using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Synthetic;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Integrations.Synthetic;

public class SyntheticVisitFeedTests
{
    private static readonly DateTimeOffset Noon = Snapshots.Noon;

    [Fact]
    public void ExternalIds_AreUnique_AcrossEveryRecipient()
    {
        var feed = SyntheticVisitFeed.Build(
            [Snapshots.VigdisRecipient, Snapshots.TorRecipient],
            Noon
        );

        var externalIds = feed.Select(snapshot => snapshot.ExternalId).ToList();

        Assert.Equal(externalIds.Count, externalIds.Distinct(StringComparer.Ordinal).Count());
    }

    // A demo that reshuffles its own history on restart would make an unchanged
    // second sync run impossible to demonstrate.
    [Fact]
    public void TheFeed_IsTheSame_WhenBuiltTwiceForTheSameInstant()
    {
        var first = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon);
        var second = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon);

        Assert.Equal(first, second);
    }

    // The id is the upsert key, so it has to name the same visit tomorrow as it
    // does today. An id counting positions in a rolling window would slide one
    // day per restart and overwrite the wrong row.
    [Fact]
    public void AVisitKeepsItsExternalId_WhenTheFeedIsRebuiltOnAnotherDay()
    {
        var today = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon);
        var tomorrow = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon.AddDays(1));

        var byId = tomorrow.ToDictionary(snapshot => snapshot.ExternalId, StringComparer.Ordinal);
        var shared = today.Where(snapshot => byId.ContainsKey(snapshot.ExternalId)).ToList();

        Assert.True(shared.Count > 10, "the windows should overlap by most of their days");
        Assert.All(
            shared,
            snapshot => Assert.Equal(snapshot.ScheduledAt, byId[snapshot.ExternalId].ScheduledAt)
        );
    }

    [Fact]
    public void TheWindowRollsForward_WhenTheFeedIsRebuiltOnAnotherDay()
    {
        var today = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon);
        var tomorrow = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon.AddDays(1));

        Assert.Equal(today.Count, tomorrow.Count);
        Assert.Contains(tomorrow, snapshot => snapshot.ScheduledAt > today.Max(t => t.ScheduledAt));
    }

    [Fact]
    public void EveryRecipient_GetsTheirOwnVisits()
    {
        var feed = SyntheticVisitFeed.Build(
            [Snapshots.VigdisRecipient, Snapshots.TorRecipient],
            Noon
        );

        var byRecipient = feed.GroupBy(snapshot => snapshot.CareRecipient)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(2, byRecipient.Count);
        Assert.Equal(byRecipient[Snapshots.Vigdis], byRecipient[Snapshots.Tor]);
    }

    [Fact]
    public void AVisitInTheFuture_IsPlannedAndNotYetCarriedOut()
    {
        var feed = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon);

        var future = feed.Where(snapshot => snapshot.ScheduledAt >= Noon).ToList();

        Assert.NotEmpty(future);
        Assert.All(
            future,
            snapshot =>
            {
                Assert.Equal(VisitStatus.Planned, snapshot.Status);
                Assert.Null(snapshot.ActualAt);
            }
        );
    }

    [Fact]
    public void AVisitInThePast_IsFinished()
    {
        var feed = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon);

        var past = feed.Where(snapshot => snapshot.ScheduledAt < Noon).ToList();

        Assert.NotEmpty(past);
        Assert.All(
            past,
            snapshot =>
                Assert.Contains(
                    snapshot.Status,
                    (VisitStatus[])[VisitStatus.Completed, VisitStatus.Missed]
                )
        );
    }

    [Fact]
    public void TheFeed_ContainsBothCompletedAndMissedVisits()
    {
        var feed = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon);

        Assert.Contains(feed, snapshot => snapshot.Status == VisitStatus.Completed);
        Assert.Contains(feed, snapshot => snapshot.Status == VisitStatus.Missed);
    }

    // The tie the continuation token exists to walk past has to occur in the
    // data the demo actually serves, not only in a hand-built test case.
    [Fact]
    public void PlannedVisits_ShareOneSourceUpdatedAt_PerRecipient()
    {
        var feed = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon);

        var plannedTimestamps = feed.Where(snapshot => snapshot.Status == VisitStatus.Planned)
            .Select(snapshot => snapshot.SourceUpdatedAt)
            .Distinct()
            .ToList();

        Assert.Single(plannedTimestamps);
    }

    [Fact]
    public void EverySourceUpdatedAt_IsAtOrBeforeTheInstantTheFeedWasBuilt()
    {
        var feed = SyntheticVisitFeed.Build([Snapshots.VigdisRecipient], Noon);

        Assert.All(feed, snapshot => Assert.True(snapshot.SourceUpdatedAt <= Noon));
    }

    [Fact]
    public void NoRecipients_ProducesNoSnapshots()
    {
        Assert.Empty(SyntheticVisitFeed.Build([], Noon));
    }

    // A snapshot that moves earlier is one paging has already walked past and
    // will skip. Finishing the 08:00 visit puts it at 08:05, so publication has
    // to stay below that; built at 07:00, where that visit can still move.
    [Fact]
    public void APlannedVisit_IsPublishedBefore_WhereFinishingItWouldMoveIt()
    {
        var recipients = Enumerable
            .Range(0, 500)
            .Select(number => new SyntheticRecipient($"recipient-{number:D4}", Snapshots.Vigdis))
            .ToList();

        var planned = SyntheticVisitFeed
            .Build(recipients, Snapshots.Midnight.AddHours(7))
            .Where(snapshot => snapshot.Status == VisitStatus.Planned)
            .ToList();

        Assert.NotEmpty(planned);
        Assert.All(
            planned,
            snapshot =>
                Assert.True(
                    snapshot.SourceUpdatedAt < snapshot.ScheduledAt.AddMinutes(5),
                    $"{snapshot.ExternalId} is published at {snapshot.SourceUpdatedAt:O}, which finishing it would move it back from."
                )
        );
    }

    // The upsert writes CareRecipientId through ExternalId, so ids counting
    // positions would hand one person's visits to the next person along.
    [Fact]
    public void AddingARecipient_LeavesEveryOtherExternalIdAlone()
    {
        var before = SyntheticVisitFeed.Build(
            [Snapshots.VigdisRecipient, Snapshots.TorRecipient],
            Noon
        );

        var after = SyntheticVisitFeed.Build(
            [Snapshots.VigdisRecipient, Snapshots.KariRecipient, Snapshots.TorRecipient],
            Noon
        );

        var torBefore = ExternalIdsFor(before, Snapshots.Tor);

        Assert.NotEmpty(torBefore);
        Assert.Equal(torBefore, ExternalIdsFor(after, Snapshots.Tor));
        Assert.Equal(
            ExternalIdsFor(before, Snapshots.Vigdis),
            ExternalIdsFor(after, Snapshots.Vigdis)
        );
    }

    private static List<string> ExternalIdsFor(
        IReadOnlyList<VisitSnapshot> feed,
        NationalIdentifier careRecipient
    ) =>
        [
            .. feed.Where(snapshot => snapshot.CareRecipient == careRecipient)
                .Select(snapshot => snapshot.ExternalId)
                .Order(StringComparer.Ordinal),
        ];

    // Postgres holds timestamptz to the microsecond, so a feed with finer
    // values would report Updated on every run. Built off an instant carrying
    // stray ticks, which is what a real clock hands over.
    [Fact]
    public void EveryTimestamp_FitsThePrecisionPostgresCanStore()
    {
        var feed = SyntheticVisitFeed.Build(
            [Snapshots.VigdisRecipient, Snapshots.TorRecipient],
            Noon.AddTicks(3)
        );

        Assert.All(
            feed,
            snapshot =>
            {
                Assert.Equal(0, snapshot.SourceUpdatedAt.Ticks % 10);
                Assert.Equal(0, snapshot.ScheduledAt.Ticks % 10);
            }
        );

        Assert.All(
            feed.Where(snapshot => snapshot.ActualAt is not null),
            snapshot => Assert.Equal(0, snapshot.ActualAt!.Value.Ticks % 10)
        );
    }
}
