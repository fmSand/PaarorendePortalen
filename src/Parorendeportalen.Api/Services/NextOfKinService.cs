using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Repositories;

namespace Parorendeportalen.Api.Services;

public sealed class NextOfKinService(
    IKinshipRegistry registry,
    NationalIdHasher nationalIdHasher) : INextOfKinService
{
    public async Task<NextOfKinResponse?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken)
    {
        var nextOfKin = await registry.GetByExternalIdAsync(externalId, cancellationToken);
        return nextOfKin?.ToResponse();
    }

    public async Task<int?> GetCareRecipientIdByExternalIdAsync(string externalId, CancellationToken cancellationToken)
    {
        var nextOfKin = await registry.GetByExternalIdAsync(externalId, cancellationToken);
        return nextOfKin?.CareRecipientId;
    }

    public async Task<NextOfKinResponse?> ResolveOrBindAsync(
        string externalId, string nationalId, string displayName, CancellationToken cancellationToken)
    {
        var existing = await registry.GetByExternalIdAsync(externalId, cancellationToken);
        if (existing is not null)
        {
            if (existing.DisplayName != displayName)
            {
                existing.DisplayName = displayName;
                await registry.UpdateAsync(existing, cancellationToken);
            }
            return existing.ToResponse();
        }

        var nationalIdHash = nationalIdHasher.Hash(nationalId);
        var grant = await registry.GetByNationalIdHashAsync(nationalIdHash, cancellationToken);
        if (grant is null)
        {
            return null;
        }

        grant.ExternalId = externalId;
        grant.DisplayName = displayName;
        await registry.UpdateAsync(grant, cancellationToken);
        return grant.ToResponse();
    }
}
