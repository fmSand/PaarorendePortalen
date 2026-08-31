using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Repositories;

namespace Parorendeportalen.Api.Services;

public sealed class CareRecipientService(ICareRecipientRepository repository) : ICareRecipientService
{
    public async Task<IReadOnlyList<CareRecipientResponse>> GetByIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        var careRecipients = await repository.GetByIdsAsync(ids, cancellationToken);
        return careRecipients.Select(c => c.ToResponse()).ToList();
    }

    public async Task<CareRecipientResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var careRecipient = await repository.GetByIdAsync(id, cancellationToken);
        return careRecipient?.ToResponse();
    }
}
