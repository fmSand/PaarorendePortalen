using Parorendeportalen.Api.Dtos;

namespace Parorendeportalen.Api.Services;

public interface ICareRecipientService
{
    Task<IReadOnlyList<CareRecipientResponse>> GetByIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken);

    Task<CareRecipientResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
