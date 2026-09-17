using Parorendeportalen.Api.Dtos.Kinship;
using Parorendeportalen.Api.Repositories.Kinship;

namespace Parorendeportalen.Api.Services.Kinship;

public sealed class CareRecipientService(ICareRecipientRepository repository)
    : ICareRecipientService
{
    public async Task<IReadOnlyList<CareRecipientResponse>> GetByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken
    )
    {
        var careRecipients = await repository.GetByIdsAsync(ids, cancellationToken);
        return careRecipients.Select(c => c.ToResponse()).ToList();
    }

    public async Task<CareRecipientResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken
    )
    {
        var careRecipient = await repository.GetByIdAsync(id, cancellationToken);
        return careRecipient?.ToResponse();
    }
}
