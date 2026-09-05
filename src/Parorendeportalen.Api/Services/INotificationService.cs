using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Services;

public interface INotificationService
{
    // Scope comes from the access policy, which has already logged it.
    Task<NotificationInboxResponse> GetInboxAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        CancellationToken cancellationToken
    );

    Task<bool> MarkReadAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        long notificationId,
        CancellationToken cancellationToken
    );

    Task MarkAllReadAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        CancellationToken cancellationToken
    );

    // Every kind, default filled in where no row exists.
    Task<IReadOnlyList<NotificationPreferenceResponse>> GetPreferencesAsync(
        int nextOfKinId,
        CancellationToken cancellationToken
    );

    Task SetPreferenceAsync(
        int nextOfKinId,
        ChangeKind kind,
        bool enabled,
        CancellationToken cancellationToken
    );
}
