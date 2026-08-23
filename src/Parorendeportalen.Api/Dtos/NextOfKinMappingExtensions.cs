using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public static class NextOfKinMappingExtensions
{
    public static NextOfKinResponse ToResponse(this NextOfKin nextOfKin) => new(
        nextOfKin.Id,
        nextOfKin.DisplayName,
        nextOfKin.CareRecipientId);
}
