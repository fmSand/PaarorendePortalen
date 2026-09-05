using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Notifications;

public sealed class EfChangeEventStore(AppDbContext context) : IChangeEventStore
{
    public async Task<IReadOnlyList<ChangeEvent>> GetUnprocessedAsync(
        int limit,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        return await context
            .ChangeEvents.Where(c => c.ProcessedAt == null)
            .OrderBy(c => c.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task DeliverAsync(
        IReadOnlyList<ChangeEvent> events,
        IReadOnlyList<Notification> notifications,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(notifications);

        foreach (var change in events)
        {
            change.ProcessedAt = processedAt;
        }

        context.Notifications.AddRange(notifications);
        await context.SaveChangesAsync(cancellationToken);
    }
}
