using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public static class NextOfKinMappingExtensions
{
    public static NextOfKinResponse ToResponse(this NextOfKin nextOfKin) => new(
        nextOfKin.Id,
        nextOfKin.DisplayName,
        nextOfKin.Grants
            .OrderBy(g => g.CareRecipient.Name)
            .Select(g => g.ToResponse())
            .ToList());

    public static KinshipGrantResponse ToResponse(this KinshipGrant grant) => new(
        grant.CareRecipientId,
        grant.CareRecipient.Name,
        grant.Relationship);
}
