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

    // Consent only. The policy checks kinship.
    Task<IReadOnlyList<ConsentScope>> GetActiveScopesAsync(
        int nextOfKinId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken
    );

    // Both gates in one query: consent for the category and a grant on the care
    // recipient, both at asOf.
    Task<IReadOnlyList<int>> GetConsentedNextOfKinIdsAsync(
        int careRecipientId,
        DataCategory category,
        DateTimeOffset asOf,
        CancellationToken cancellationToken
    );
}
