using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Integrations.Sync;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Integrations.Sync;

public class VisitSyncServiceTests
{
    private static readonly DateTimeOffset Noon = Snapshots.Noon;

    private readonly IVisitIngestionStore _ingestionStore = Substitute.For<IVisitIngestionStore>();
    private readonly ICareRecipientRepository _careRecipients =
        Substitute.For<ICareRecipientRepository>();
    private readonly NationalIdHasher _hasher = new("test-pepper");
    private readonly Dictionary<string, int> _known = new(StringComparer.Ordinal);
    private readonly List<IReadOnlyList<Visit>> _upserted = [];
    private readonly VisitSyncService _sut;

    public VisitSyncServiceTests()
    {
        // Distinct counters per batch, so an accumulator wired to the wrong
        // field cannot hide behind two zeroes.
        _ingestionStore
            .UpsertAsync(Arg.Any<IReadOnlyList<Visit>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var batch = call.Arg<IReadOnlyList<Visit>>();
                _upserted.Add(batch);
                return new VisitIngestionResult(batch.Count, batch.Count * 10, batch.Count * 100);
            });

        _careRecipients
            .GetIdsByNationalIdHashesAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var asked = call.Arg<IReadOnlyCollection<string>>();
                return (IReadOnlyDictionary<string, int>)
                    asked
                        .Where(_known.ContainsKey)
                        .ToDictionary(hash => hash, hash => _known[hash], StringComparer.Ordinal);
            });

        _sut = new VisitSyncService(
            _ingestionStore,
            _careRecipients,
            _hasher,
            NullLogger<VisitSyncService>.Instance
        );
    }

    private void Know(NationalIdentifier careRecipient, int careRecipientId) =>
        _known[_hasher.Hash(careRecipient.HashInput)] = careRecipientId;

    private Task<VisitSyncOutcome> RunAsync(IVisitSource source, SyncPosition? resumeFrom = null) =>
        _sut.RunAsync(source, resumeFrom ?? SyncPosition.Start, CancellationToken.None);

    [Fact]
    public async Task ARunWithoutAWatermark_AsksTheSourceForEverything()
    {
        var source = new ScriptedVisitSource(() => ScriptedVisitSource.LastPage());

        await RunAsync(source);

        var cursor = Assert.Single(source.Cursors);
        Assert.Null(cursor.ChangedSince);
        Assert.Null(cursor.ContinuationToken);
    }

    [Fact]
    public async Task ARunWithAWatermark_AsksOnlyForWhatChangedSinceIt()
    {
        var source = new ScriptedVisitSource(() => ScriptedVisitSource.LastPage());

        await RunAsync(source, new SyncPosition(Noon, null));

        Assert.Equal(Noon, Assert.Single(source.Cursors).ChangedSince);
    }

    // Resuming a run the page cap cut short. Starting over from the watermark
    // would re-read the pages already taken and never reach the tail.
    [Fact]
    public async Task ARunResumingFromAToken_HandsThatTokenBackToTheSource()
    {
        var source = new ScriptedVisitSource(() => ScriptedVisitSource.LastPage());

        await RunAsync(source, new SyncPosition(Noon, "token-7"));

        var cursor = Assert.Single(source.Cursors);
        Assert.Equal(Noon, cursor.ChangedSince);
        Assert.Equal("token-7", cursor.ContinuationToken);
    }

    [Fact]
    public async Task AResolvedSnapshot_BecomesAVisitCarryingItsProvenance()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var scheduledAt = Noon.AddHours(1);
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(
                Snapshots.Visit(
                    "visit-0001",
                    Noon,
                    scheduledAt: scheduledAt,
                    actualAt: scheduledAt.AddMinutes(5),
                    status: VisitStatus.Completed,
                    notes: "Morgenstell."
                )
            )
        );

        await RunAsync(source);

        var visit = Assert.Single(Assert.Single(_upserted));
        Assert.Equal(7, visit.CareRecipientId);
        Assert.Equal("visit-0001", visit.ExternalId);
        Assert.Equal(Origin.Synthetic, visit.Origin);
        Assert.Equal(scheduledAt, visit.ScheduledAt);
        Assert.Equal(scheduledAt.AddMinutes(5), visit.ActualAt);
        Assert.Equal(VisitStatus.Completed, visit.Status);
        Assert.Equal("Hjemmetjenesten Oslo", visit.CaregiverName);
        Assert.Equal("Morgenstell.", visit.Notes);
    }

    // Origin.Portal is what protects a row a next-of-kin wrote. Ingestion must
    // never be handed one.
    [Fact]
    public async Task NoMappedVisit_EverCarriesThePortalOrigin()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(Snapshots.Visit("visit-0001", Noon))
        );

        await RunAsync(source);

        Assert.All(
            _upserted.SelectMany(batch => batch),
            visit => Assert.NotEqual(Origin.Portal, visit.Origin)
        );
    }

    [Fact]
    public async Task ASnapshotForSomeoneThePortalDoesNotHold_IsSkippedAndCounted()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(
                Snapshots.Visit("visit-0001", Noon, Snapshots.Vigdis),
                Snapshots.Visit("visit-0002", Noon.AddMinutes(1), Snapshots.Tor)
            )
        );

        var outcome = await RunAsync(source);

        Assert.Equal(1, outcome.UnresolvedSnapshots);
        var visit = Assert.Single(Assert.Single(_upserted));
        Assert.Equal("visit-0001", visit.ExternalId);
    }

    [Fact]
    public async Task APageWhereNothingResolves_StillAdvancesNothingAndIsCounted()
    {
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(
                Snapshots.Visit("visit-0001", Noon, Snapshots.Tor),
                Snapshots.Visit("visit-0002", Noon.AddMinutes(5), Snapshots.Tor)
            )
        );

        var outcome = await RunAsync(source);

        Assert.Equal(2, outcome.UnresolvedSnapshots);
        Assert.Empty(Assert.Single(_upserted));
        Assert.Equal(Noon, outcome.Position!.SourceUpdatedThrough);
    }

    [Fact]
    public async Task TheWatermark_MovesToTheNewestSnapshotSeen_WhenEverythingResolved()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var newest = Noon.AddMinutes(30);
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(
                Snapshots.Visit("visit-0001", Noon),
                Snapshots.Visit("visit-0002", newest),
                Snapshots.Visit("visit-0003", Noon.AddMinutes(10))
            )
        );

        var outcome = await RunAsync(source);

        Assert.Equal(newest, outcome.Position!.SourceUpdatedThrough);
        Assert.Null(outcome.Position.ContinuationToken);
        Assert.False(outcome.Truncated);
    }

    // Held back on purpose: those visits arrive on their own once the care
    // recipient is seeded.
    [Fact]
    public async Task TheWatermark_IsHeldBack_ToTheOldestSnapshotThatDidNotResolve()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var unresolvedAt = Noon.AddMinutes(10);
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(
                Snapshots.Visit("visit-0001", Noon, Snapshots.Vigdis),
                Snapshots.Visit("visit-0002", unresolvedAt, Snapshots.Tor),
                Snapshots.Visit("visit-0003", Noon.AddMinutes(30), Snapshots.Vigdis)
            )
        );

        var outcome = await RunAsync(source);

        Assert.Equal(unresolvedAt, outcome.Position!.SourceUpdatedThrough);
    }

    // The unresolved snapshot is for a visit still to come, so its ScheduledAt
    // is ahead of every SourceUpdatedAt in the page. Holding the watermark on
    // the wrong one of the two would push it forward.
    [Fact]
    public async Task TheHeldBackWatermark_ComesFromSourceUpdatedAt_NotScheduledAt()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(
                Snapshots.Visit("visit-0001", Noon.AddMinutes(30), Snapshots.Vigdis),
                Snapshots.Visit("visit-0002", Noon, Snapshots.Tor, scheduledAt: Noon.AddDays(3))
            )
        );

        var outcome = await RunAsync(source);

        Assert.Equal(Noon, outcome.Position!.SourceUpdatedThrough);
    }

    [Fact]
    public async Task ARunThatReadNothing_LeavesTheWatermarkWhereItWas()
    {
        var source = new ScriptedVisitSource(() => ScriptedVisitSource.LastPage());

        var outcome = await RunAsync(source, new SyncPosition(Noon, null));

        Assert.Null(outcome.Position);
    }

    // Leaving the token behind means the source keeps filtering out everything
    // between the watermark and it, for good.
    [Fact]
    public async Task ARunThatReadNothing_StillClearsATokenItResumedFrom()
    {
        var source = new ScriptedVisitSource(() => ScriptedVisitSource.LastPage());

        var outcome = await RunAsync(source, new SyncPosition(Noon, "token-7"));

        Assert.NotNull(outcome.Position);
        Assert.Null(outcome.Position.ContinuationToken);
        Assert.Null(outcome.Position.SourceUpdatedThrough);
        Assert.False(outcome.Truncated);
    }

    // The next run starts past what did not resolve here, so the holdback has to
    // be stored the way the token is or those visits never arrive.
    [Fact]
    public async Task ATruncatedRun_CarriesTheHoldbackOutWithItsToken()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var unresolvedAt = Noon.AddMinutes(10);
        var source = new ScriptedVisitSource(() =>
            new VisitSnapshotPage(
                [
                    Snapshots.Visit("visit-0001", Noon, Snapshots.Vigdis),
                    Snapshots.Visit("visit-0002", unresolvedAt, Snapshots.Tor),
                ],
                "always-more"
            )
        );

        var outcome = await RunAsync(source, new SyncPosition(Noon, null));

        Assert.True(outcome.Truncated);
        Assert.Equal(unresolvedAt, outcome.Position!.UnresolvedFrom);
        Assert.Equal(Noon, outcome.Position.SourceUpdatedThrough);
    }

    // Everything this run sees resolves, so without the stored holdback the
    // watermark would jump past what the run before it could not place.
    [Fact]
    public async Task ARunResumingWithAHoldback_KeepsTheWatermarkBehindIt()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var unresolvedAt = Noon.AddMinutes(10);
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(Snapshots.Visit("visit-0009", Noon.AddHours(9)))
        );

        var outcome = await RunAsync(source, new SyncPosition(Noon, "token-7", unresolvedAt));

        Assert.Equal(unresolvedAt, outcome.Position!.SourceUpdatedThrough);
    }

    // The watermark now sits at or before it, so the next run derives it again.
    // Carrying it would pin the watermark there after the recipient is seeded.
    [Fact]
    public async Task ADrainedRun_LeavesNoHoldbackBehind()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(Snapshots.Visit("visit-0009", Noon.AddHours(9)))
        );

        var outcome = await RunAsync(source, new SyncPosition(Noon, null, Noon.AddMinutes(10)));

        Assert.Null(outcome.Position!.UnresolvedFrom);
    }

    // The second run would otherwise quietly raise the floor the first one set.
    [Fact]
    public async Task ATruncatedRun_KeepsTheOlderOfTheTwoHoldbacks()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var source = new ScriptedVisitSource(() =>
            new VisitSnapshotPage(
                [Snapshots.Visit("visit-0002", Noon.AddHours(2), Snapshots.Tor)],
                "always-more"
            )
        );

        var outcome = await RunAsync(source, new SyncPosition(Noon, null, Noon.AddMinutes(10)));

        Assert.Equal(Noon.AddMinutes(10), outcome.Position!.UnresolvedFrom);
    }

    [Fact]
    public async Task TheRun_FollowsTheContinuationToken_UntilThePagesRunOut()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var source = new ScriptedVisitSource(
            () => new VisitSnapshotPage([Snapshots.Visit("visit-0001", Noon)], "token-1"),
            () =>
                new VisitSnapshotPage(
                    [Snapshots.Visit("visit-0002", Noon.AddMinutes(1))],
                    "token-2"
                ),
            () => ScriptedVisitSource.LastPage(Snapshots.Visit("visit-0003", Noon.AddMinutes(2)))
        );

        var outcome = await RunAsync(source);

        Assert.Equal(3, outcome.Ingestion.Inserted);
        Assert.Equal(
            ["visit-0001", "visit-0002", "visit-0003"],
            _upserted.SelectMany(batch => batch).Select(visit => visit.ExternalId)
        );
        Assert.Equal([null, "token-1", "token-2"], source.Cursors.Select(c => c.ContinuationToken));
    }

    // An empty page can still have pages behind it, when a source filters one
    // down to nothing. Stopping there would strand the rest.
    [Fact]
    public async Task AnEmptyPageThatReportsMore_DoesNotEndTheRun()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var source = new ScriptedVisitSource(
            () => new VisitSnapshotPage([], "token-1"),
            () => ScriptedVisitSource.LastPage(Snapshots.Visit("visit-0001", Noon))
        );

        var outcome = await RunAsync(source);

        Assert.Equal(1, outcome.Ingestion.Inserted);
        Assert.Equal(Noon, outcome.Position!.SourceUpdatedThrough);
    }

    [Fact]
    public async Task TheRun_SumsEveryIngestionCounterAcrossPages()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var source = new ScriptedVisitSource(
            () =>
                new VisitSnapshotPage(
                    [
                        Snapshots.Visit("visit-0001", Noon),
                        Snapshots.Visit("visit-0002", Noon.AddMinutes(1)),
                    ],
                    "token-1"
                ),
            () => ScriptedVisitSource.LastPage(Snapshots.Visit("visit-0003", Noon.AddMinutes(2)))
        );

        var outcome = await RunAsync(source);

        Assert.Equal(new VisitIngestionResult(3, 30, 300), outcome.Ingestion);
    }

    // A source that never stops issuing tokens must not hold the run open. What
    // is left resumes from the token on the next run.
    [Fact(Timeout = 30000)]
    public async Task ASourceThatAlwaysReportsMore_StopsAtThePageCap_AndLeavesATokenBehind()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var source = new ScriptedVisitSource(() =>
            new VisitSnapshotPage([Snapshots.Visit("visit-0001", Noon)], "always-more")
        );

        var outcome = await RunAsync(source, new SyncPosition(Noon, null));

        Assert.True(outcome.Truncated);
        Assert.Equal("always-more", outcome.Position!.ContinuationToken);
        Assert.Equal(Noon, outcome.Position.SourceUpdatedThrough);
        Assert.InRange(source.Cursors.Count, 2, 1000);
    }

    // The failure the worker records on the SyncRun row. Swallowing it here
    // would let the watermark advance past data nobody wrote.
    [Fact]
    public async Task AFailingSource_PropagatesOutOfTheRun()
    {
        var source = new ScriptedVisitSource(() =>
            throw new HttpRequestException("the source is down")
        );

        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync(source));
    }

    [Fact]
    public async Task OnlyOneLookupPerPage_EvenWhenAPageRepeatsTheSameRecipient()
    {
        Know(Snapshots.Vigdis, careRecipientId: 7);
        var source = new ScriptedVisitSource(() =>
            ScriptedVisitSource.LastPage(
                Snapshots.Visit("visit-0001", Noon),
                Snapshots.Visit("visit-0002", Noon.AddMinutes(1)),
                Snapshots.Visit("visit-0003", Noon.AddMinutes(2))
            )
        );

        await RunAsync(source);

        await _careRecipients
            .Received(1)
            .GetIdsByNationalIdHashesAsync(
                Arg.Is<IReadOnlyCollection<string>>(hashes => hashes.Count == 1),
                Arg.Any<CancellationToken>()
            );
    }
}
