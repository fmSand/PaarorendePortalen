using Parorendeportalen.Api.Models.Kinship;

namespace Parorendeportalen.Api.Repositories.Kinship;

public interface IKinshipRegistry
{
    Task<NextOfKin?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken);

    Task<NextOfKin?> GetByNationalIdHashAsync(
        string nationalIdHash,
        CancellationToken cancellationToken
    );

    Task UpdateAsync(NextOfKin nextOfKin, CancellationToken cancellationToken);
}
