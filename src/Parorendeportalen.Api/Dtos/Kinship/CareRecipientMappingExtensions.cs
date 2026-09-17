using Parorendeportalen.Api.Models.Kinship;

namespace Parorendeportalen.Api.Dtos.Kinship;

public static class CareRecipientMappingExtensions
{
    public static CareRecipientResponse ToResponse(this CareRecipient careRecipient) =>
        new(careRecipient.Id, careRecipient.Name);
}
