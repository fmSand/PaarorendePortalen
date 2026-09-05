using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;

namespace Parorendeportalen.Api.Services;

public sealed class NotificationService(
    INotificationRepository notifications,
    INotificationPreferenceRepository preferences,
    TimeProvider timeProvider
) : INotificationService
{
    // Enough for recent notices. The full record is the visit log.
    public const int InboxSize = 50;

    public async Task<NotificationInboxResponse> GetInboxAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        CancellationToken cancellationToken
    )
    {
        var inbox = await notifications.GetInboxAsync(
            nextOfKinId,
            scope,
            InboxSize,
            cancellationToken
        );

        return new NotificationInboxResponse(
            inbox.Items.Select(n => n.ToResponse()).ToList(),
            inbox.UnreadCount
        );
    }

    public Task<bool> MarkReadAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        long notificationId,
        CancellationToken cancellationToken
    ) =>
        notifications.MarkReadAsync(
            nextOfKinId,
            scope,
            notificationId,
            timeProvider.GetUtcNow(),
            cancellationToken
        );

    public Task MarkAllReadAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        CancellationToken cancellationToken
    ) =>
        notifications.MarkAllReadAsync(
            nextOfKinId,
            scope,
            timeProvider.GetUtcNow(),
            cancellationToken
        );

    public async Task<IReadOnlyList<NotificationPreferenceResponse>> GetPreferencesAsync(
        int nextOfKinId,
        CancellationToken cancellationToken
    )
    {
        var chosen = (await preferences.GetAsync(nextOfKinId, cancellationToken)).ToDictionary(
            p => p.Kind,
            p => p.Enabled
        );

        return Enum.GetValues<ChangeKind>()
            .Select(kind => new NotificationPreferenceResponse(
                kind,
                chosen.GetValueOrDefault(kind, true)
            ))
            .ToList();
    }

    public Task SetPreferenceAsync(
        int nextOfKinId,
        ChangeKind kind,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return preferences.SetAsync(nextOfKinId, kind, enabled, cancellationToken);
    }
}
