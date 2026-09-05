using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public class EfNotificationRepositoryTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = Snapshots.Noon;

    private PostgresTestDatabase _factory = null!;
    private long _nextEventId;
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

    private async Task<long> SeedAsync(
        int nextOfKinId,
        int careRecipientId,
        DateTimeOffset occurredAt,
        DataCategory category = DataCategory.Visits,
        ChangeKind kind = ChangeKind.Completed,
        DateTimeOffset? readAt = null
    )
    {
        using var context = _factory.CreateContext();
        var notification = new Notification
        {
            NextOfKinId = nextOfKinId,
            CareRecipientId = careRecipientId,
            ChangeEventId = ++_nextEventId,
            Category = category,
            Kind = kind,
            VisitId = 42,
            ScheduledAt = occurredAt.AddHours(1),
            OccurredAt = occurredAt,
            ReadAt = readAt,
        };
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();

        return notification.Id;
    }

    private static ConsentScope Visits(int careRecipientId) =>
        new(careRecipientId, DataCategory.Visits);

    private async Task<NotificationInbox> InboxOf(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        int limit = 50
    )
    {
        using var context = _factory.CreateContext();
        var sut = new EfNotificationRepository(context);
        return await sut.GetInboxAsync(nextOfKinId, scope, limit, CancellationToken.None);
    }

    private async Task<bool> MarkReadAsync(
        int nextOfKinId,
        long id,
        IReadOnlyList<ConsentScope>? scope = null
    )
    {
        using var context = _factory.CreateContext();
        var sut = new EfNotificationRepository(context);
        return await sut.MarkReadAsync(
            nextOfKinId,
            scope ?? EverythingSeeded,
            id,
            Now,
            CancellationToken.None
        );
    }

    private async Task<int> MarkAllReadAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope>? scope = null
    )
    {
        using var context = _factory.CreateContext();
        var sut = new EfNotificationRepository(context);
        return await sut.MarkAllReadAsync(
            nextOfKinId,
            scope ?? EverythingSeeded,
            Now,
            CancellationToken.None
        );
    }

    private IReadOnlyList<ConsentScope> EverythingSeeded =>
        [
            Visits(_vigdisId),
            Visits(_torId),
            new ConsentScope(_vigdisId, DataCategory.Medications),
            new ConsentScope(_torId, DataCategory.Medications),
        ];

    private async Task<Notification> RowAsync(long id)
    {
        using var context = _factory.CreateContext();
        return await context.Notifications.AsNoTracking().SingleAsync(n => n.Id == id);
    }

    [Fact]
    public async Task ReturnsOnlyRowsInsideTheScope()
    {
        var inScope = await SeedAsync(_fridaId, _vigdisId, Now);
        await SeedAsync(_fridaId, _torId, Now);
        await SeedAsync(_fridaId, _vigdisId, Now, DataCategory.Medications);

        var inbox = await InboxOf(_fridaId, [Visits(_vigdisId)]);

        Assert.Equal(inScope, Assert.Single(inbox.Items).Id);
    }

    // The badge must not reveal a change in a category that is not shared.
    [Fact]
    public async Task CountsUnread_OverTheScopeOnly()
    {
        await SeedAsync(_fridaId, _vigdisId, Now);
        await SeedAsync(_fridaId, _vigdisId, Now.AddMinutes(-1), readAt: Now);
        await SeedAsync(_fridaId, _torId, Now);
        await SeedAsync(_fridaId, _vigdisId, Now, DataCategory.Medications);

        var inbox = await InboxOf(_fridaId, [Visits(_vigdisId)]);

        Assert.Equal(1, inbox.UnreadCount);
        Assert.Equal(2, inbox.Items.Count);
    }

    [Fact]
    public async Task IsScopedToTheNextOfKin()
    {
        await SeedAsync(_fabianId, _vigdisId, Now);

        var inbox = await InboxOf(_fridaId, [Visits(_vigdisId)]);

        Assert.Empty(inbox.Items);
        Assert.Equal(0, inbox.UnreadCount);
    }

    [Fact]
    public async Task AnEmptyScope_ReturnsNothing()
    {
        await SeedAsync(_fridaId, _vigdisId, Now);

        var inbox = await InboxOf(_fridaId, []);

        Assert.Empty(inbox.Items);
        Assert.Equal(0, inbox.UnreadCount);
    }

    [Fact]
    public async Task ListsNewestFirst_AcrossCareRecipientsAndCategories()
    {
        var oldest = await SeedAsync(_fridaId, _vigdisId, Now.AddMinutes(-2));
        var newest = await SeedAsync(_fridaId, _torId, Now, DataCategory.Medications);
        var middle = await SeedAsync(_fridaId, _vigdisId, Now.AddMinutes(-1));

        var inbox = await InboxOf(
            _fridaId,
            [Visits(_vigdisId), new ConsentScope(_torId, DataCategory.Medications)]
        );

        Assert.Equal([newest, middle, oldest], inbox.Items.Select(n => n.Id));
    }

    // Two rows from one tick share an OccurredAt. Id keeps order stable.
    [Fact]
    public async Task BreaksATie_OnId_NewestFirst()
    {
        var first = await SeedAsync(_fridaId, _vigdisId, Now);
        var second = await SeedAsync(_fridaId, _vigdisId, Now);

        var inbox = await InboxOf(_fridaId, [Visits(_vigdisId)]);

        Assert.Equal([second, first], inbox.Items.Select(n => n.Id));
    }

    [Fact]
    public async Task TakesAtMostTheLimit_AcrossCategories_AndStillCountsEveryUnread()
    {
        await SeedAsync(_fridaId, _vigdisId, Now.AddMinutes(-3));
        var b = await SeedAsync(_fridaId, _vigdisId, Now.AddMinutes(-2), DataCategory.Medications);
        var c = await SeedAsync(_fridaId, _vigdisId, Now.AddMinutes(-1));
        var d = await SeedAsync(_fridaId, _vigdisId, Now, DataCategory.Medications);

        var inbox = await InboxOf(
            _fridaId,
            [Visits(_vigdisId), new ConsentScope(_vigdisId, DataCategory.Medications)],
            limit: 3
        );

        Assert.Equal([d, c, b], inbox.Items.Select(n => n.Id));
        Assert.Equal(4, inbox.UnreadCount);
    }

    [Fact]
    public async Task CarriesTheCareRecipientName()
    {
        await SeedAsync(_fridaId, _vigdisId, Now);

        var inbox = await InboxOf(_fridaId, [Visits(_vigdisId)]);

        Assert.Equal("Vigdis Quist", Assert.Single(inbox.Items).CareRecipient.Name);
    }

    [Fact]
    public async Task MarkRead_StampsTheCallersOwnRow()
    {
        var id = await SeedAsync(_fridaId, _vigdisId, Now.AddMinutes(-5));

        Assert.True(await MarkReadAsync(_fridaId, id));

        Assert.Equal(Now, (await RowAsync(id)).ReadAt);
    }

    [Fact]
    public async Task MarkRead_RefusesAnotherPersonsRow_AndLeavesItUnread()
    {
        var id = await SeedAsync(_fabianId, _vigdisId, Now.AddMinutes(-5));

        Assert.False(await MarkReadAsync(_fridaId, id));

        Assert.Null((await RowAsync(id)).ReadAt);
    }

    [Fact]
    public async Task MarkRead_ReportsFalse_ForAMissingId()
    {
        Assert.False(await MarkReadAsync(_fridaId, 999));
    }

    [Fact]
    public async Task MarkRead_KeepsTheFirstReadTime_WhenMarkedAgain()
    {
        var firstRead = Now.AddDays(-1);
        var id = await SeedAsync(_fridaId, _vigdisId, Now.AddDays(-2), readAt: firstRead);

        Assert.True(await MarkReadAsync(_fridaId, id));

        Assert.Equal(firstRead, (await RowAsync(id)).ReadAt);
    }

    [Fact]
    public async Task MarkAllRead_StampsEveryUnreadRowOfTheCaller_AndNobodyElses()
    {
        var unreadA = await SeedAsync(_fridaId, _vigdisId, Now.AddMinutes(-3));
        var unreadB = await SeedAsync(_fridaId, _torId, Now.AddMinutes(-2));
        var fabians = await SeedAsync(_fabianId, _vigdisId, Now.AddMinutes(-1));

        Assert.Equal(2, await MarkAllReadAsync(_fridaId));

        Assert.Equal(Now, (await RowAsync(unreadA)).ReadAt);
        Assert.Equal(Now, (await RowAsync(unreadB)).ReadAt);
        Assert.Null((await RowAsync(fabians)).ReadAt);
    }

    [Fact]
    public async Task MarkAllRead_LeavesAnAlreadyReadRowsTimeAlone()
    {
        var firstRead = Now.AddDays(-1);
        var id = await SeedAsync(_fridaId, _vigdisId, Now.AddDays(-2), readAt: firstRead);

        Assert.Equal(0, await MarkAllReadAsync(_fridaId));

        Assert.Equal(firstRead, (await RowAsync(id)).ReadAt);
    }

    [Fact]
    public async Task MarkRead_RefusesARowOutsideTheScope_AndLeavesItUnread()
    {
        var id = await SeedAsync(_fridaId, _vigdisId, Now.AddMinutes(-5));

        Assert.False(await MarkReadAsync(_fridaId, id, [Visits(_torId)]));

        Assert.Null((await RowAsync(id)).ReadAt);
    }

    [Fact]
    public async Task MarkRead_RefusesARowInAnUnconsentedCategory()
    {
        var id = await SeedAsync(_fridaId, _vigdisId, Now, DataCategory.Medications);

        Assert.False(await MarkReadAsync(_fridaId, id, [Visits(_vigdisId)]));

        Assert.Null((await RowAsync(id)).ReadAt);
    }

    [Fact]
    public async Task MarkAllRead_StampsOnlyRowsInsideTheScope()
    {
        var inScope = await SeedAsync(_fridaId, _vigdisId, Now.AddMinutes(-3));
        var otherRecipient = await SeedAsync(_fridaId, _torId, Now.AddMinutes(-2));
        var otherCategory = await SeedAsync(
            _fridaId,
            _vigdisId,
            Now.AddMinutes(-1),
            DataCategory.Medications
        );

        Assert.Equal(1, await MarkAllReadAsync(_fridaId, [Visits(_vigdisId)]));

        Assert.Equal(Now, (await RowAsync(inScope)).ReadAt);
        Assert.Null((await RowAsync(otherRecipient)).ReadAt);
        Assert.Null((await RowAsync(otherCategory)).ReadAt);
    }

    [Fact]
    public async Task MarkAllRead_WithAnEmptyScope_StampsNothing()
    {
        var id = await SeedAsync(_fridaId, _vigdisId, Now);

        Assert.Equal(0, await MarkAllReadAsync(_fridaId, []));

        Assert.Null((await RowAsync(id)).ReadAt);
    }
}
