using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public sealed record VisitResponse(
    int Id,
    int CareRecipientId,
    string CareRecipientName,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? ActualAt,
    VisitStatus Status,
    ServiceType? ServiceType,
    string? CaregiverName,
    string? Notes,
    string? Title,
    Origin Origin,
    int? CreatedByNextOfKinId,
    string? CreatedByName,
    Visibility? Visibility,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    // What If-Match must carry to change this row. Postgres xmin, opaque to the client.
    uint Version
);
