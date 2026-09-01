using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public static class VisitMappingExtensions
{
    public static VisitResponse ToResponse(this Visit visit) =>
        new(
            visit.Id,
            visit.CareRecipientId,
            visit.CareRecipient.Name,
            visit.ScheduledAt,
            visit.ActualAt,
            visit.Status,
            visit.CaregiverName,
            visit.Notes
        );
}
