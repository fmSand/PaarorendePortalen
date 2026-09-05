using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public interface INotificationPreferenceRepository
{
    Task<IReadOnlyList<NotificationPreference>> GetAsync(
        int nextOfKinId,
        CancellationToken cancellationToken
    );

    Task SetAsync(
        int nextOfKinId,
        ChangeKind kind,
        bool enabled,
        CancellationToken cancellationToken
    );

    Task<IReadOnlySet<(int NextOfKinId, ChangeKind Kind)>> GetDisabledAsync(
        IReadOnlyCollection<int> nextOfKinIds,
        CancellationToken cancellationToken
    );
}
