using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public interface ICareRecipientRepository
{
    Task<IReadOnlyList<CareRecipient>> GetAllAsync(CancellationToken cancellationToken);

    Task<CareRecipient?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
