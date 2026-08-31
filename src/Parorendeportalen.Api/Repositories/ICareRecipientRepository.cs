using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public interface ICareRecipientRepository
{
    Task<IReadOnlyList<CareRecipient>> GetByIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken);

    Task<CareRecipient?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
