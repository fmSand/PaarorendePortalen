using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public sealed record NotificationInbox(IReadOnlyList<Notification> Items, int UnreadCount);

public interface INotificationRepository
{
    // The count is over the scope too, so the badge cannot reveal a change in
    // an unshared category.
    Task<NotificationInbox> GetInboxAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        int limit,
        CancellationToken cancellationToken
    );

    // Marking a read row again is a no-op that still reports true.
    Task<bool> MarkReadAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        long notificationId,
        DateTimeOffset readAt,
        CancellationToken cancellationToken
    );

    // Scoped like the inbox. A revoked consent hides its notices, and marking
    // all read must not settle them unseen.
    Task<int> MarkAllReadAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        DateTimeOffset readAt,
        CancellationToken cancellationToken
    );
}
