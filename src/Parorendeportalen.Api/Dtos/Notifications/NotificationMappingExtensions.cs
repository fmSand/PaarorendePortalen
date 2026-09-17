using Parorendeportalen.Api.Models.Notifications;

namespace Parorendeportalen.Api.Dtos.Notifications;

public static class NotificationMappingExtensions
{
    public static NotificationResponse ToResponse(this Notification notification) =>
        new(
            notification.Id,
            notification.CareRecipientId,
            notification.CareRecipient.Name,
            notification.Category,
            notification.Kind,
            notification.VisitId,
            notification.ScheduledAt,
            notification.OccurredAt,
            notification.ReadAt
        );
}
