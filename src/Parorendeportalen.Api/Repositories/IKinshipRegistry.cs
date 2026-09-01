using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public interface IKinshipRegistry
{
    Task<NextOfKin?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken);

    Task<NextOfKin?> GetByNationalIdHashAsync(
        string nationalIdHash,
        CancellationToken cancellationToken
    );

    Task UpdateAsync(NextOfKin nextOfKin, CancellationToken cancellationToken);
}
