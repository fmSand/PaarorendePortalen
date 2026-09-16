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
            visit.ServiceType,
            visit.CaregiverName,
            visit.Notes,
            visit.Title,
            visit.Origin,
            visit.CreatedByNextOfKinId,
            visit.CreatedBy?.DisplayName,
            visit.Visibility,
            visit.CreatedAt,
            visit.UpdatedAt,
            visit.Version
        );
}
