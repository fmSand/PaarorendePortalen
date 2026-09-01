using Parorendeportalen.Api.Dtos;

namespace Parorendeportalen.Api.Services;

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
