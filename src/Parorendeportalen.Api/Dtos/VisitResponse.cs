using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public sealed record VisitResponse(
    int Id,
    int CareRecipientId,
    string CareRecipientName,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? ActualAt,
    VisitStatus Status,
    string? CaregiverName,
    string? Notes
);
