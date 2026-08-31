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

    public async Task<IReadOnlyList<int>> GetCareRecipientIdsByExternalIdAsync(
        string externalId, CancellationToken cancellationToken)
    {
        var nextOfKin = await registry.GetByExternalIdAsync(externalId, cancellationToken);

        return nextOfKin is null
            ? []
            : nextOfKin.Grants.Select(g => g.CareRecipientId).ToList();
    }

    public async Task<NextOfKinResponse?> ResolveOrBindAsync(
        string externalId, string nationalId, string displayName, CancellationToken cancellationToken)
    {
        var person = await registry.GetByExternalIdAsync(externalId, cancellationToken)
            ?? await BindByNationalIdAsync(externalId, nationalId, displayName, cancellationToken);

        if (person is null)
        {
            return null;
        }

        if (person.Grants.Count == 0)
        {
            return null;
        }

        if (person.DisplayName != displayName)
        {
            person.DisplayName = displayName;
            await registry.UpdateAsync(person, cancellationToken);
        }

        return person.ToResponse();
    }

    // Binds sub to a row that already exists, found by seeded national id hash.
    // Login never creates a person - see ADR-0003
    private async Task<Models.NextOfKin?> BindByNationalIdAsync(
        string externalId, string nationalId, string displayName, CancellationToken cancellationToken)
    {
        var nationalIdHash = nationalIdHasher.Hash(nationalId);
        var person = await registry.GetByNationalIdHashAsync(nationalIdHash, cancellationToken);

        if (person is null)
        {
            return null;
        }

        person.ExternalId = externalId;
        person.DisplayName = displayName;
        await registry.UpdateAsync(person, cancellationToken);

        return person;
    }
}
