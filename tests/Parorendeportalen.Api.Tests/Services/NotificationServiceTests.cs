using NSubstitute;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;
using Parorendeportalen.Api.Services;
using Parorendeportalen.Api.Tests.TestHelpers;

namespace Parorendeportalen.Api.Tests.Services;

public class NotificationServiceTests
{
    private const int NextOfKinId = 5;
    private const int CareRecipientId = 7;

    private readonly INotificationRepository _notifications =
        Substitute.For<INotificationRepository>();
    private readonly INotificationPreferenceRepository _preferences =
        Substitute.For<INotificationPreferenceRepository>();
    private readonly FixedTimeProvider _clock = new(Snapshots.Noon);
    private readonly NotificationService _sut;

    public NotificationServiceTests()
    {
        _sut = new NotificationService(_notifications, _preferences, _clock);
    }

    private static Notification Row(long id, DateTimeOffset? readAt = null) =>
        new()
        {
            Id = id,
            NextOfKinId = NextOfKinId,
            CareRecipientId = CareRecipientId,
            CareRecipient = new CareRecipient { Name = "Vigdis Quist" },
            ChangeEventId = 100 + id,
            Category = DataCategory.Visits,
            Kind = ChangeKind.Completed,
            VisitId = 42,
            ScheduledAt = Snapshots.Noon.AddHours(-3),
            OccurredAt = Snapshots.Noon.AddHours(-1),
            ReadAt = readAt,
        };

    [Fact]
    public async Task GetInbox_PassesTheScopeAndTheInboxSizeDown_AndMapsWhatComesBack()
    {
        IReadOnlyList<ConsentScope> scope =
        [
            new ConsentScope(CareRecipientId, DataCategory.Visits),
        ];
        _notifications
            .GetInboxAsync(
                NextOfKinId,
                scope,
                NotificationService.InboxSize,
                Arg.Any<CancellationToken>()
            )
            .Returns(new NotificationInbox([Row(1), Row(2, readAt: Snapshots.Noon)], 1));

        var inbox = await _sut.GetInboxAsync(NextOfKinId, scope, CancellationToken.None);

        Assert.Equal(1, inbox.UnreadCount);
        Assert.Equal([1L, 2L], inbox.Items.Select(n => n.Id));
        var first = inbox.Items[0];
        Assert.Equal(CareRecipientId, first.CareRecipientId);
        Assert.Equal("Vigdis Quist", first.CareRecipientName);
        Assert.Equal(DataCategory.Visits, first.Category);
        Assert.Equal(ChangeKind.Completed, first.Kind);
        Assert.Equal(42, first.VisitId);
        Assert.Equal(Snapshots.Noon.AddHours(-3), first.ScheduledAt);
        Assert.Equal(Snapshots.Noon.AddHours(-1), first.OccurredAt);
        Assert.Null(first.ReadAt);
        Assert.Equal(Snapshots.Noon, inbox.Items[1].ReadAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MarkRead_StampsTheClocksNow_AndReportsWhatTheRepositorySaid(bool marked)
    {
        IReadOnlyList<ConsentScope> scope = [new(CareRecipientId, DataCategory.Visits)];
        _notifications
            .MarkReadAsync(NextOfKinId, scope, 9, Snapshots.Noon, Arg.Any<CancellationToken>())
            .Returns(marked);

        Assert.Equal(
            marked,
            await _sut.MarkReadAsync(NextOfKinId, scope, 9, CancellationToken.None)
        );
    }

    [Fact]
    public async Task MarkAllRead_StampsTheClocksNow_ForTheCallersScope()
    {
        IReadOnlyList<ConsentScope> scope = [new(CareRecipientId, DataCategory.Visits)];

        await _sut.MarkAllReadAsync(NextOfKinId, scope, CancellationToken.None);

        await _notifications
            .Received(1)
            .MarkAllReadAsync(NextOfKinId, scope, Snapshots.Noon, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPreferences_ListsEveryKind_EnabledUnlessChosenOtherwise()
    {
        _preferences
            .GetAsync(NextOfKinId, Arg.Any<CancellationToken>())
            .Returns([
                new NotificationPreference { Kind = ChangeKind.Completed, Enabled = false },
                new NotificationPreference { Kind = ChangeKind.Missed, Enabled = true },
            ]);

        var preferences = await _sut.GetPreferencesAsync(NextOfKinId, CancellationToken.None);

        Assert.Equal(
            Enum.GetValues<ChangeKind>().OrderBy(kind => kind),
            preferences.Select(p => p.Kind).OrderBy(kind => kind)
        );
        Assert.False(preferences.Single(p => p.Kind == ChangeKind.Completed).Enabled);
        Assert.All(
            preferences.Where(p => p.Kind != ChangeKind.Completed),
            p => Assert.True(p.Enabled)
        );
    }

    [Fact]
    public async Task SetPreference_PassesTheChoiceThrough()
    {
        await _sut.SetPreferenceAsync(NextOfKinId, ChangeKind.Added, false, CancellationToken.None);

        await _preferences
            .Received(1)
            .SetAsync(NextOfKinId, ChangeKind.Added, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPreference_RefusesAKindThatDoesNotExist()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _sut.SetPreferenceAsync(NextOfKinId, (ChangeKind)42, true, CancellationToken.None)
        );

        await _preferences
            .DidNotReceive()
            .SetAsync(
                Arg.Any<int>(),
                Arg.Any<ChangeKind>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }
}
