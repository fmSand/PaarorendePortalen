using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public sealed class EfNotificationPreferenceRepository(AppDbContext context)
    : INotificationPreferenceRepository
{
    public async Task<IReadOnlyList<NotificationPreference>> GetAsync(
        int nextOfKinId,
        CancellationToken cancellationToken
    ) =>
        await context
            .NotificationPreferences.AsNoTracking()
            .Where(p => p.NextOfKinId == nextOfKinId)
            .ToListAsync(cancellationToken);

    public async Task SetAsync(
        int nextOfKinId,
        ChangeKind kind,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        var preference = await context.NotificationPreferences.FirstOrDefaultAsync(
            p => p.NextOfKinId == nextOfKinId && p.Kind == kind,
            cancellationToken
        );

        if (preference is null)
        {
            context.NotificationPreferences.Add(
                new NotificationPreference
                {
                    NextOfKinId = nextOfKinId,
                    Kind = kind,
                    Enabled = enabled,
                }
            );
        }
        else
        {
            preference.Enabled = enabled;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<(int NextOfKinId, ChangeKind Kind)>> GetDisabledAsync(
        IReadOnlyCollection<int> nextOfKinIds,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(nextOfKinIds);

        if (nextOfKinIds.Count == 0)
        {
            return new HashSet<(int, ChangeKind)>();
        }

        var ids = nextOfKinIds.ToList();
        var disabled = await context
            .NotificationPreferences.AsNoTracking()
            .Where(p => !p.Enabled && ids.Contains(p.NextOfKinId))
            .Select(p => new { p.NextOfKinId, p.Kind })
            .ToListAsync(cancellationToken);

        return disabled.Select(p => (p.NextOfKinId, p.Kind)).ToHashSet();
    }
}
