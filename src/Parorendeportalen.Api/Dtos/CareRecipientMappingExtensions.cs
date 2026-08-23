using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Dtos;

public static class CareRecipientMappingExtensions
{
    public static CareRecipientResponse ToResponse(this CareRecipient careRecipient) => new(
        careRecipient.Id,
        careRecipient.Name);
}
