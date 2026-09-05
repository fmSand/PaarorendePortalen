using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Notifications;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Notifications;

// Against Postgres with the consent join: the two gates are a query, and a stub would only assert the stub.
[Collection(PostgresCollection.Name)]
public class NotificationFanOutTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = Snapshots.Noon;
    private static readonly DateTimeOffset Earlier = Now.AddHours(-1);

    private PostgresTestDatabase _factory = null!;
    private int _fridaId;
    private int _fabianId;
    private int _vigdisId;
    private int _torId;

    public async Task InitializeAsync()
    {
        _factory = await PostgresTestDatabase.CreateAsync(fixture.ConnectionString);

        using var context = _factory.CreateContext();
        var frida = new NextOfKin { NationalIdHash = "hash-frida", DisplayName = "Frida Sand" };
        var fabian = new NextOfKin { NationalIdHash = "hash-fabian", DisplayName = "Fabian Quist" };
        var vigdis = new CareRecipient { Name = "Vigdis Quist" };
        var tor = new CareRecipient { Name = "Tor Quist" };
        context.AddRange(frida, fabian, vigdis, tor);
        await context.SaveChangesAsync();

        _fridaId = frida.Id;
        _fabianId = fabian.Id;
        _vigdisId = vigdis.Id;
        _torId = tor.Id;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private async Task GrantAsync(
        int nextOfKinId,
        int careRecipientId,
        DateTimeOffset? validTo = null,
        DateTimeOffset? validFrom = null
    )
    {
        using var context = _factory.CreateContext();
        context.KinshipGrants.Add(
            new KinshipGrant
            {
                NextOfKinId = nextOfKinId,
                CareRecipientId = careRecipientId,
                ValidFrom = validFrom ?? Now.AddDays(-1),
                ValidTo = validTo,
            }
        );
        await context.SaveChangesAsync();
    }

    private async Task ConsentAsync(
        int nextOfKinId,
        int careRecipientId,
        DataCategory category = DataCategory.Visits,
        DateTimeOffset? validTo = null,
        DateTimeOffset? validFrom = null
    )
    {
        using var context = _factory.CreateContext();
        context.Consents.Add(
            new Consent
            {
                NextOfKinId = nextOfKinId,
                CareRecipientId = careRecipientId,
                Category = category,
                ValidFrom = validFrom ?? Now.AddDays(-1),
                ValidTo = validTo,
            }
        );
        await context.SaveChangesAsync();
    }

    private async Task EntitleAsync(int nextOfKinId, int careRecipientId)
    {
        await GrantAsync(nextOfKinId, careRecipientId);
        await ConsentAsync(nextOfKinId, careRecipientId);
    }

    private async Task PreferAsync(int nextOfKinId, ChangeKind kind, bool enabled)
    {
        using var context = _factory.CreateContext();
        context.NotificationPreferences.Add(
            new NotificationPreference
            {
                NextOfKinId = nextOfKinId,
                Kind = kind,
                Enabled = enabled,
            }
        );
        await context.SaveChangesAsync();
    }

    private async Task<long> EventAsync(
        int careRecipientId,
        ChangeKind kind = ChangeKind.Completed,
        DateTimeOffset? scheduledAt = null,
        DateTimeOffset? occurredAt = null,
        DataCategory category = DataCategory.Visits
    )
    {
        using var context = _factory.CreateContext();
        var change = new ChangeEvent
        {
            CareRecipientId = careRecipientId,
            Category = category,
            Kind = kind,
            ScheduledAt = scheduledAt ?? Earlier.AddHours(-2),
            OccurredAt = occurredAt ?? Earlier,
        };
        context.ChangeEvents.Add(change);
        await context.SaveChangesAsync();

        return change.Id;
    }

    private async Task<FanOutResult> DeliverAsync(int batchSize = 100)
    {
        using var context = _factory.CreateContext();
        var sut = new NotificationFanOut(
            new EfChangeEventStore(context),
            new EfConsentRepository(context),
            new EfNotificationPreferenceRepository(context),
            new NotificationOptions { BatchSize = batchSize },
            new FixedTimeProvider(Now)
        );

        return await sut.DeliverPendingAsync(CancellationToken.None);
    }

    private async Task<List<Notification>> InboxOf(int nextOfKinId)
    {
        using var context = _factory.CreateContext();

        return await context
            .Notifications.AsNoTracking()
            .Where(n => n.NextOfKinId == nextOfKinId)
            .OrderBy(n => n.Id)
            .ToListAsync();
    }

    private async Task<List<ChangeEvent>> EventsAsync()
    {
        using var context = _factory.CreateContext();

        return await context.ChangeEvents.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
    }

    [Fact]
    public async Task DeliversToANextOfKin_HoldingAGrantAndAConsent_AsACopyOfTheEvent()
    {
        await EntitleAsync(_fridaId, _vigdisId);
        var eventId = await EventAsync(
            _vigdisId,
            ChangeKind.Completed,
            scheduledAt: Earlier.AddHours(-2)
        );

        var result = await DeliverAsync();

        Assert.Equal(new FanOutResult(1, 1), result);
        var notification = Assert.Single(await InboxOf(_fridaId));
        Assert.Equal(eventId, notification.ChangeEventId);
        Assert.Equal(_vigdisId, notification.CareRecipientId);
        Assert.Equal(DataCategory.Visits, notification.Category);
        Assert.Equal(ChangeKind.Completed, notification.Kind);
        Assert.Equal(Earlier.AddHours(-2), notification.ScheduledAt);
        Assert.Equal(Earlier, notification.OccurredAt);
        Assert.Null(notification.ReadAt);
    }

    [Fact]
    public async Task SkipsANextOfKin_WhoHoldsAGrantButNoConsent()
    {
        await GrantAsync(_fridaId, _vigdisId);
        await EventAsync(_vigdisId);

        var result = await DeliverAsync();

        Assert.Equal(new FanOutResult(1, 0), result);
        Assert.Empty(await InboxOf(_fridaId));
    }

    // A stale open consent under a closed grant opens nothing, the same order the policy checks in.
    [Fact]
    public async Task SkipsANextOfKin_WhoseGrantIsClosed_EvenWithAnOpenConsent()
    {
        await GrantAsync(_fridaId, _vigdisId, validTo: Earlier.AddMinutes(-1));
        await ConsentAsync(_fridaId, _vigdisId);
        await EventAsync(_vigdisId);

        await DeliverAsync();

        Assert.Empty(await InboxOf(_fridaId));
    }

    [Fact]
    public async Task SkipsANextOfKin_WhoseConsentIsClosed()
    {
        await GrantAsync(_fridaId, _vigdisId);
        await ConsentAsync(_fridaId, _vigdisId, validTo: Earlier.AddMinutes(-1));
        await EventAsync(_vigdisId);

        await DeliverAsync();

        Assert.Empty(await InboxOf(_fridaId));
    }

    [Fact]
    public async Task SkipsAChange_FromBeforeTheConsentOpened()
    {
        await GrantAsync(_fridaId, _vigdisId);
        await ConsentAsync(_fridaId, _vigdisId, validFrom: Earlier.AddMinutes(1));
        await EventAsync(_vigdisId, occurredAt: Earlier);

        var result = await DeliverAsync();

        Assert.Equal(new FanOutResult(1, 0), result);
        Assert.Empty(await InboxOf(_fridaId));
    }

    [Fact]
    public async Task SkipsAChange_FromBeforeTheGrantOpened()
    {
        await GrantAsync(_fridaId, _vigdisId, validFrom: Earlier.AddMinutes(1));
        await ConsentAsync(_fridaId, _vigdisId);
        await EventAsync(_vigdisId, occurredAt: Earlier);

        await DeliverAsync();

        Assert.Empty(await InboxOf(_fridaId));
    }

    [Fact]
    public async Task DeliversAChange_FromWhileTheConsentWasOpen_ThoughItClosedBeforeTheFanOut()
    {
        await GrantAsync(_fridaId, _vigdisId);
        await ConsentAsync(_fridaId, _vigdisId, validTo: Earlier.AddMinutes(1));
        await EventAsync(_vigdisId, occurredAt: Earlier);

        var result = await DeliverAsync();

        Assert.Equal(new FanOutResult(1, 1), result);
        Assert.Single(await InboxOf(_fridaId));
    }

    [Fact]
    public async Task GatesEachChangeAtItsOwnInstant_WithinOneBatch()
    {
        await GrantAsync(_fridaId, _vigdisId);
        await ConsentAsync(_fridaId, _vigdisId, validFrom: Earlier);
        await EventAsync(_vigdisId, ChangeKind.Completed, occurredAt: Earlier.AddMinutes(-1));
        var covered = await EventAsync(_vigdisId, ChangeKind.Missed, occurredAt: Earlier);

        var result = await DeliverAsync();

        Assert.Equal(new FanOutResult(2, 1), result);
        Assert.Equal(covered, Assert.Single(await InboxOf(_fridaId)).ChangeEventId);
    }

    [Fact]
    public async Task SkipsANextOfKin_WhoseConsentCoversAnotherCategory()
    {
        await GrantAsync(_fridaId, _vigdisId);
        await ConsentAsync(_fridaId, _vigdisId, DataCategory.Medications);
        await EventAsync(_vigdisId, category: DataCategory.Visits);

        await DeliverAsync();

        Assert.Empty(await InboxOf(_fridaId));
    }

    [Fact]
    public async Task SkipsANextOfKin_WhoseEntitlementIsOnAnotherCareRecipient()
    {
        await EntitleAsync(_fridaId, _torId);
        await EventAsync(_vigdisId);

        await DeliverAsync();

        Assert.Empty(await InboxOf(_fridaId));
    }

    [Fact]
    public async Task FansOneChangeOutToEveryEntitledNextOfKin()
    {
        await EntitleAsync(_fridaId, _vigdisId);
        await EntitleAsync(_fabianId, _vigdisId);
        var eventId = await EventAsync(_vigdisId);

        var result = await DeliverAsync();

        Assert.Equal(new FanOutResult(1, 2), result);
        Assert.Equal(eventId, Assert.Single(await InboxOf(_fridaId)).ChangeEventId);
        Assert.Equal(eventId, Assert.Single(await InboxOf(_fabianId)).ChangeEventId);
    }

    [Fact]
    public async Task SkipsAKindThePersonSwitchedOff_AndDeliversTheOthers()
    {
        await EntitleAsync(_fridaId, _vigdisId);
        await PreferAsync(_fridaId, ChangeKind.Completed, enabled: false);
        await EventAsync(_vigdisId, ChangeKind.Completed);
        await EventAsync(_vigdisId, ChangeKind.Missed);

        var result = await DeliverAsync();

        Assert.Equal(new FanOutResult(2, 1), result);
        Assert.Equal(ChangeKind.Missed, Assert.Single(await InboxOf(_fridaId)).Kind);
    }

    [Fact]
    public async Task APreference_MutesOnlyThePersonWhoSetIt()
    {
        await EntitleAsync(_fridaId, _vigdisId);
        await EntitleAsync(_fabianId, _vigdisId);
        await PreferAsync(_fridaId, ChangeKind.Completed, enabled: false);
        await EventAsync(_vigdisId, ChangeKind.Completed);

        await DeliverAsync();

        Assert.Empty(await InboxOf(_fridaId));
        Assert.Single(await InboxOf(_fabianId));
    }

    [Fact]
    public async Task AnExplicitlyEnabledPreference_StillDelivers()
    {
        await EntitleAsync(_fridaId, _vigdisId);
        await PreferAsync(_fridaId, ChangeKind.Completed, enabled: true);
        await EventAsync(_vigdisId, ChangeKind.Completed);

        await DeliverAsync();

        Assert.Single(await InboxOf(_fridaId));
    }

    [Fact]
    public async Task MarksEveryEventProcessed_DeliveredOrNot()
    {
        await EntitleAsync(_fridaId, _vigdisId);
        await EventAsync(_vigdisId);
        await EventAsync(_torId);

        await DeliverAsync();

        var events = await EventsAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, change => Assert.Equal(Now, change.ProcessedAt));
    }

    [Fact]
    public async Task AnAddedVisitAlreadyInThePast_IsProcessedWithoutDelivery()
    {
        await EntitleAsync(_fridaId, _vigdisId);
        await EventAsync(
            _vigdisId,
            ChangeKind.Added,
            scheduledAt: Earlier.AddMinutes(-1),
            occurredAt: Earlier
        );

        var result = await DeliverAsync();

        Assert.Equal(new FanOutResult(1, 0), result);
        Assert.Empty(await InboxOf(_fridaId));
        Assert.NotNull(Assert.Single(await EventsAsync()).ProcessedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    public async Task AnAddedVisitStillToCome_IsDelivered(int minutesAhead)
    {
        await EntitleAsync(_fridaId, _vigdisId);
        await EventAsync(
            _vigdisId,
            ChangeKind.Added,
            scheduledAt: Earlier.AddMinutes(minutesAhead),
            occurredAt: Earlier
        );

        await DeliverAsync();

        Assert.Single(await InboxOf(_fridaId));
    }

    // Only Added is filtered on time. A visit completed late still gets a notice.
    [Fact]
    public async Task ACompletedVisitInThePast_IsDelivered()
    {
        await EntitleAsync(_fridaId, _vigdisId);
        await EventAsync(
            _vigdisId,
            ChangeKind.Completed,
            scheduledAt: Earlier.AddHours(-3),
            occurredAt: Earlier
        );

        await DeliverAsync();

        Assert.Single(await InboxOf(_fridaId));
    }

    [Fact]
    public async Task ASecondRun_FindsNothingToDo()
    {
        await EntitleAsync(_fridaId, _vigdisId);
        await EventAsync(_vigdisId);
        await DeliverAsync();

        var result = await DeliverAsync();

        Assert.Equal(new FanOutResult(0, 0), result);
        Assert.Single(await InboxOf(_fridaId));
    }

    [Fact]
    public async Task TakesAtMostABatch_OldestFirst_AndLeavesTheRest()
    {
        await EntitleAsync(_fridaId, _vigdisId);
        var first = await EventAsync(_vigdisId, ChangeKind.Completed);
        var second = await EventAsync(_vigdisId, ChangeKind.Missed);
        var third = await EventAsync(_vigdisId, ChangeKind.Cancelled);

        var result = await DeliverAsync(batchSize: 2);

        Assert.Equal(new FanOutResult(2, 2), result);
        Assert.Equal([first, second], (await InboxOf(_fridaId)).Select(n => n.ChangeEventId));
        var events = await EventsAsync();
        Assert.Null(events.Single(c => c.Id == third).ProcessedAt);
    }

    [Fact]
    public async Task NothingPending_IsANoOp()
    {
        await EntitleAsync(_fridaId, _vigdisId);

        Assert.Equal(new FanOutResult(0, 0), await DeliverAsync());
    }
}
