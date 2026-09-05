using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public sealed class EfNotificationRepository(AppDbContext context) : INotificationRepository
{
    public async Task<NotificationInbox> GetInboxAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        int limit,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var items = new List<Notification>();
        var unread = 0;

        // A list of pairs does not translate, so one query per category.
        foreach (var group in scope.GroupBy(pair => pair.Category))
        {
            var careRecipientIds = group.Select(pair => pair.CareRecipientId).ToList();
            var inScope = context
                .Notifications.AsNoTracking()
                .Where(n =>
                    n.NextOfKinId == nextOfKinId
                    && n.Category == group.Key
                    && careRecipientIds.Contains(n.CareRecipientId)
                );

            items.AddRange(
                await inScope
                    .Include(n => n.CareRecipient)
                    .OrderByDescending(n => n.OccurredAt)
                    .ThenByDescending(n => n.Id)
                    .Take(limit)
                    .ToListAsync(cancellationToken)
            );
            unread += await inScope.CountAsync(n => n.ReadAt == null, cancellationToken);
        }

        return new NotificationInbox(
            items
                .OrderByDescending(n => n.OccurredAt)
                .ThenByDescending(n => n.Id)
                .Take(limit)
                .ToList(),
            unread
        );
    }

    public async Task<bool> MarkReadAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        long notificationId,
        DateTimeOffset readAt,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(scope);

        // Both ids in the query, so another person's row is never fetched. Then
        // the consent scope, so a row the inbox hides cannot be marked read.
        var notification = await context.Notifications.FirstOrDefaultAsync(
            n => n.Id == notificationId && n.NextOfKinId == nextOfKinId,
            cancellationToken
        );

        if (notification is null || !scope.Contains(ScopeOf(notification)))
        {
            return false;
        }

        if (notification.ReadAt is null)
        {
            notification.ReadAt = readAt;
            await context.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<int> MarkAllReadAsync(
        int nextOfKinId,
        IReadOnlyList<ConsentScope> scope,
        DateTimeOffset readAt,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(scope);

        var marked = 0;

        // One update per category, same reason as the inbox read.
        foreach (var group in scope.GroupBy(pair => pair.Category))
        {
            var careRecipientIds = group.Select(pair => pair.CareRecipientId).ToList();

            marked += await context
                .Notifications.Where(n =>
                    n.NextOfKinId == nextOfKinId
                    && n.ReadAt == null
                    && n.Category == group.Key
                    && careRecipientIds.Contains(n.CareRecipientId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(n => n.ReadAt, readAt),
                    cancellationToken
                );
        }

        return marked;
    }

    private static ConsentScope ScopeOf(Notification notification) =>
        new(notification.CareRecipientId, notification.Category);
}
