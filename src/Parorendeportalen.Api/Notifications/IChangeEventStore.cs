using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Notifications;

public interface IChangeEventStore
{
    // Oldest first. Tracked, so DeliverAsync on the same store can stamp them.
    Task<IReadOnlyList<ChangeEvent>> GetUnprocessedAsync(
        int limit,
        CancellationToken cancellationToken
    );

    // One save: notifications and ProcessedAt stamps land together or not at
    // all (makes a tick repeated after a crash safe).
    Task DeliverAsync(
        IReadOnlyList<ChangeEvent> events,
        IReadOnlyList<Notification> notifications,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken
    );
}
