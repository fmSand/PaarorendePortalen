using Parorendeportalen.Api.Dtos.Kinship;

namespace Parorendeportalen.Api.Services.Kinship;

public interface ICareRecipientService
{
    Task<IReadOnlyList<CareRecipientResponse>> GetByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken
    );

    Task<CareRecipientResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
