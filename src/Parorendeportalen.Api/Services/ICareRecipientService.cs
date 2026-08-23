using Parorendeportalen.Api.Dtos;

namespace Parorendeportalen.Api.Services;

public interface ICareRecipientService
{
    Task<IReadOnlyList<CareRecipientResponse>> GetAllAsync(CancellationToken cancellationToken);

    Task<CareRecipientResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
