using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public interface IConsentRepository
{
    Task<IReadOnlyList<DataCategory>> GetActiveCategoriesAsync(
        int nextOfKinId,
        int careRecipientId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken
    );
}
