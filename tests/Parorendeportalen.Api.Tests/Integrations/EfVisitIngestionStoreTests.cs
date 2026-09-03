using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Integrations;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Integrations;

[Collection(PostgresCollection.Name)]
public class EfVisitIngestionStoreTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = Snapshots.Noon;

    private PostgresTestDatabase _factory = null!;
    private int _careRecipientId;
    private int _otherCareRecipientId;

    public async Task InitializeAsync()
    {
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

        using var context = _factory.CreateContext();
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        var tor = new CareRecipient { Name = "Tor Quist" };
        context.CareRecipients.AddRange(vigdis, tor);
        await context.SaveChangesAsync();

        _careRecipientId = vigdis.Id;
        _otherCareRecipientId = tor.Id;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private Visit Incoming(
        string externalId,
        DateTimeOffset? scheduledAt = null,
        DateTimeOffset? actualAt = null,
        VisitStatus status = VisitStatus.Planned,
        string? caregiverName = "Hjemmetjenesten Oslo",
        string? notes = null,
        int? careRecipientId = null,
        Origin origin = Origin.Synthetic
    ) =>
        new()
        {
            CareRecipientId = careRecipientId ?? _careRecipientId,
            ScheduledAt = scheduledAt ?? Noon,
            ActualAt = actualAt,
            Status = status,
            CaregiverName = caregiverName,
            Notes = notes,
            Origin = origin,
            ExternalId = externalId,
        };

    private async Task<VisitIngestionResult> UpsertAsync(params Visit[] visits)
    {
        using var context = _factory.CreateContext();
        var sut = new EfVisitIngestionStore(context);

        return await sut.UpsertAsync(visits, CancellationToken.None);
    }

    // The third upsert catches a store that counts Updated without writing.
    private async Task<Visit> UpsertThenChangeAsync(Func<Visit> original, Func<Visit> changed)
    {
        Assert.Equal(new VisitIngestionResult(1, 0, 0), await UpsertAsync(original()));
        Assert.Equal(new VisitIngestionResult(0, 1, 0), await UpsertAsync(changed()));
        Assert.Equal(new VisitIngestionResult(0, 0, 1), await UpsertAsync(changed()));

        using var context = _factory.CreateContext();

        return await context.Visits.SingleAsync();
    }

    [Fact]
    public async Task AFirstRun_InsertsEverySnapshotRow()
    {
        var result = await UpsertAsync(Incoming("visit-0001"), Incoming("visit-0002"));

        Assert.Equal(new VisitIngestionResult(2, 0, 0), result);

        using var context = _factory.CreateContext();
        Assert.Equal(2, await context.Visits.CountAsync());
    }

    // The whole point of the (ExternalId, Origin) key. Without it a second run
    // would duplicate every visit.
    [Fact]
    public async Task ASecondRunOverTheSameData_ReportsOnlyUnchanged()
    {
        await UpsertAsync(Incoming("visit-0001"), Incoming("visit-0002"));

        var result = await UpsertAsync(Incoming("visit-0001"), Incoming("visit-0002"));

        Assert.Equal(new VisitIngestionResult(0, 0, 2), result);

        using var context = _factory.CreateContext();
        Assert.Equal(2, await context.Visits.CountAsync());
    }

    [Fact]
    public async Task AChangedStatus_IsStored()
    {
        var stored = await UpsertThenChangeAsync(
            () => Incoming("visit-0001", status: VisitStatus.Planned),
            () => Incoming("visit-0001", status: VisitStatus.Missed)
        );

        Assert.Equal(VisitStatus.Missed, stored.Status);
    }

    [Fact]
    public async Task AChangedActualAt_IsStored()
    {
        var stored = await UpsertThenChangeAsync(
            () => Incoming("visit-0001", actualAt: null),
            () => Incoming("visit-0001", actualAt: Noon.AddMinutes(5))
        );

        Assert.Equal(Noon.AddMinutes(5), stored.ActualAt);
    }

    [Fact]
    public async Task AChangedNote_IsStored()
    {
        var stored = await UpsertThenChangeAsync(
            () => Incoming("visit-0001", notes: "Morgenstell."),
            () => Incoming("visit-0001", notes: "Morgenstell. Ny avtale satt.")
        );

        Assert.Equal("Morgenstell. Ny avtale satt.", stored.Notes);
    }

    [Fact]
    public async Task AChangedScheduledAt_IsStored()
    {
        var stored = await UpsertThenChangeAsync(
            () => Incoming("visit-0001", scheduledAt: Noon),
            () => Incoming("visit-0001", scheduledAt: Noon.AddHours(1))
        );

        Assert.Equal(Noon.AddHours(1), stored.ScheduledAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Nattpatruljen")]
    public async Task AChangedCaregiverName_IsStored(string? caregiverName)
    {
        var stored = await UpsertThenChangeAsync(
            () => Incoming("visit-0001", caregiverName: "Hjemmetjenesten Oslo"),
            () => Incoming("visit-0001", caregiverName: caregiverName)
        );

        Assert.Equal(caregiverName, stored.CaregiverName);
    }

    [Fact]
    public async Task ARowMovedToAnotherCareRecipient_IsUpdatedRatherThanDuplicated()
    {
        var stored = await UpsertThenChangeAsync(
            () => Incoming("visit-0001"),
            () => Incoming("visit-0001", careRecipientId: _otherCareRecipientId)
        );

        Assert.Equal(_otherCareRecipientId, stored.CareRecipientId);

        using var context = _factory.CreateContext();
        Assert.Equal(1, await context.Visits.CountAsync());
    }

    // Postgres truncates timestamptz to the microsecond. Comparing the
    // unrounded value against the stored one would report Updated forever.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATimestampFinerThanPostgresCanHold_StillReportsUnchangedOnARerun(
        bool onActualAt
    )
    {
        var precise = Noon.AddTicks(7);
        Visit Incoming7() =>
            onActualAt
                ? Incoming("visit-0001", actualAt: precise, status: VisitStatus.Completed)
                : Incoming("visit-0001", scheduledAt: precise);

        await UpsertAsync(Incoming7());

        Assert.Equal(new VisitIngestionResult(0, 0, 1), await UpsertAsync(Incoming7()));
    }

    // Npgsql refuses a DateTimeOffset with an offset against timestamptz, so a
    // source sending Oslo local time would fail every run without this.
    [Fact]
    public async Task ATimestampCarryingAnOffset_IsStoredAsUtc_AndSettlesOnARerun()
    {
        var osloMorning = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(
            new VisitIngestionResult(1, 0, 0),
            await UpsertAsync(Incoming("visit-0001", scheduledAt: osloMorning))
        );
        Assert.Equal(
            new VisitIngestionResult(0, 0, 1),
            await UpsertAsync(Incoming("visit-0001", scheduledAt: osloMorning))
        );

        using var context = _factory.CreateContext();
        var stored = await context.Visits.SingleAsync();

        Assert.Equal(osloMorning.ToUniversalTime(), stored.ScheduledAt);
        Assert.Equal(TimeSpan.Zero, stored.ScheduledAt.Offset);
    }

    [Fact]
    public async Task TheSameInstantInAnotherOffset_IsNotAChange()
    {
        await UpsertAsync(Incoming("visit-0001", scheduledAt: Noon));

        var result = await UpsertAsync(
            Incoming("visit-0001", scheduledAt: Noon.ToOffset(TimeSpan.FromHours(2)))
        );

        Assert.Equal(new VisitIngestionResult(0, 0, 1), result);
    }

    [Fact]
    public async Task APortalRowSharingAnExternalId_IsLeftAlone()
    {
        using (var seedContext = _factory.CreateContext())
        {
            seedContext.Visits.Add(
                Incoming("shared-id", notes: "Skrevet av pårørende", origin: Origin.Portal)
            );
            await seedContext.SaveChangesAsync();
        }

        var result = await UpsertAsync(Incoming("shared-id", notes: "Fra kilden"));

        Assert.Equal(new VisitIngestionResult(1, 0, 0), result);

        using var context = _factory.CreateContext();
        var portalRow = await context.Visits.SingleAsync(v => v.Origin == Origin.Portal);
        Assert.Equal("Skrevet av pårørende", portalRow.Notes);
    }

    [Fact]
    public async Task APortalRowInTheBatch_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            UpsertAsync(Incoming("visit-0001", origin: Origin.Portal))
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AVisitWithoutTheSourcesOwnId_IsRejected(string? externalId)
    {
        var visit = Incoming("visit-0001");
        visit.ExternalId = externalId;

        await Assert.ThrowsAsync<ArgumentException>(() => UpsertAsync(visit));
    }

    [Fact]
    public async Task TheSameExternalIdTwiceInOneBatch_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            UpsertAsync(Incoming("visit-0001"), Incoming("visit-0001", notes: "Andre versjon"))
        );
    }

    [Fact]
    public async Task ARejectedBatch_WritesNothing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            UpsertAsync(Incoming("visit-0001"), Incoming("visit-0002", origin: Origin.Portal))
        );

        using var context = _factory.CreateContext();
        Assert.Empty(context.Visits);
    }

    [Fact]
    public async Task AnEmptyBatch_ReportsNothingAndWritesNothing()
    {
        var result = await UpsertAsync();

        Assert.Equal(new VisitIngestionResult(0, 0, 0), result);

        using var context = _factory.CreateContext();
        Assert.Empty(context.Visits);
    }

    [Fact]
    public async Task AMixedBatch_CountsInsertedUpdatedAndUnchangedApart()
    {
        await UpsertAsync(
            Incoming("visit-0001"),
            Incoming("visit-0002", status: VisitStatus.Planned)
        );

        var result = await UpsertAsync(
            Incoming("visit-0001"),
            Incoming("visit-0002", status: VisitStatus.Completed),
            Incoming("visit-0003")
        );

        Assert.Equal(new VisitIngestionResult(1, 1, 1), result);
    }
}
