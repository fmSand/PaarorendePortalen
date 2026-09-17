using Parorendeportalen.Api.Models.Kinship;

namespace Parorendeportalen.Api.Repositories.Kinship;

public interface ICareRecipientRepository
{
    Task<IReadOnlyList<CareRecipient>> GetByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken
    );

    Task<CareRecipient?> GetByIdAsync(int id, CancellationToken cancellationToken);

    // Batched because sync resolves a whole page at once. Takes hashes, so the
    // hash format stays on the integration side.
    Task<IReadOnlyDictionary<string, int>> GetIdsByNationalIdHashesAsync(
        IReadOnlyCollection<string> nationalIdHashes,
        CancellationToken cancellationToken
    );
}
