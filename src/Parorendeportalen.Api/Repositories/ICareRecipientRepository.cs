using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public interface ICareRecipientRepository
{
    Task<IReadOnlyList<CareRecipient>> GetByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken
    );

    Task<CareRecipient?> GetByIdAsync(int id, CancellationToken cancellationToken);

    // Batched because sync resolves a whole page at once. Takes hashes rather
    // than identifiers, so the hash format stays on the integration side.
    Task<IReadOnlyDictionary<string, int>> GetIdsByNationalIdHashesAsync(
        IReadOnlyCollection<string> nationalIdHashes,
        CancellationToken cancellationToken
    );
}
