using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public sealed record NotificationResponse(
    long Id,
    int CareRecipientId,
    string CareRecipientName,
    DataCategory Category,
    ChangeKind Kind,
    int? VisitId,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ReadAt
);

public sealed record NotificationInboxResponse(
    IReadOnlyList<NotificationResponse> Items,
    int UnreadCount
);

public sealed record NotificationPreferenceResponse(ChangeKind Kind, bool Enabled);
