using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories;

namespace Parorendeportalen.Api.Notifications;

public sealed class NotificationFanOut(
    IChangeEventStore changeEvents,
    IConsentRepository consents,
    INotificationPreferenceRepository preferences,
    NotificationOptions options,
    TimeProvider timeProvider
) : INotificationFanOut
{
    public async Task<FanOutResult> DeliverPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await changeEvents.GetUnprocessedAsync(options.BatchSize, cancellationToken);
        if (pending.Count == 0)
        {
            return new FanOutResult(0, 0);
        }

        var news = pending.Where(IsNews).ToList();

        var recipientsByScope = await RecipientsAsync(news, cancellationToken);
        var disabled = await preferences.GetDisabledAsync(
            [.. recipientsByScope.Values.SelectMany(ids => ids).Distinct()],
            cancellationToken
        );

        var notifications = new List<Notification>();

        foreach (var change in news)
        {
            var scope = new ConsentScope(change.CareRecipientId, change.Category);

            foreach (var nextOfKinId in recipientsByScope[(scope, change.OccurredAt)])
            {
                if (!disabled.Contains((nextOfKinId, change.Kind)))
                {
                    notifications.Add(ToNotification(change, nextOfKinId));
                }
            }
        }

        await changeEvents.DeliverAsync(
            pending,
            notifications,
            timeProvider.GetUtcNow(),
            cancellationToken
        );

        return new FanOutResult(pending.Count, notifications.Count);
    }

    // A visit the portal first hears about after it happened goes in the log.
    // There is nothing left to act on, so nobody is told.
    private static bool IsNews(ChangeEvent change) =>
        change.Kind != ChangeKind.Added
        || change.ScheduledAt is null
        || change.ScheduledAt >= change.OccurredAt;

    // Gated at the change's own time, so a late worker delivers the same rows
    // an on-time one would. The key holds that time; a sync batch shares one
    // OccurredAt, so lookups still collapse.
    private async Task<
        Dictionary<(ConsentScope Scope, DateTimeOffset AsOf), IReadOnlyList<int>>
    > RecipientsAsync(IReadOnlyList<ChangeEvent> news, CancellationToken cancellationToken)
    {
        var recipients = new Dictionary<(ConsentScope, DateTimeOffset), IReadOnlyList<int>>();

        foreach (var change in news)
        {
            var scope = new ConsentScope(change.CareRecipientId, change.Category);
            var key = (scope, change.OccurredAt);

            if (recipients.ContainsKey(key))
            {
                continue;
            }

            recipients[key] = await consents.GetConsentedNextOfKinIdsAsync(
                scope.CareRecipientId,
                scope.Category,
                change.OccurredAt,
                cancellationToken
            );
        }

        return recipients;
    }

    private static Notification ToNotification(ChangeEvent change, int nextOfKinId) =>
        new()
        {
            NextOfKinId = nextOfKinId,
            CareRecipientId = change.CareRecipientId,
            ChangeEventId = change.Id,
            Category = change.Category,
            Kind = change.Kind,
            VisitId = change.VisitId,
            ScheduledAt = change.ScheduledAt,
            OccurredAt = change.OccurredAt,
        };
}
