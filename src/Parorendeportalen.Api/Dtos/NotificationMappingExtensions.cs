using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

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
