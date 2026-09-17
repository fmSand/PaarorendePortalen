using Parorendeportalen.Api.Dtos.Kinship;

namespace Parorendeportalen.Api.Services.Kinship;

public interface INextOfKinService
{
    Task<NextOfKinResponse?> GetByExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<int>> GetCareRecipientIdsByExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken
    );

    Task<NextOfKinResponse?> ResolveOrBindAsync(
        string externalId,
        string nationalId,
        string displayName,
        CancellationToken cancellationToken
    );
}
