using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Services;

public interface IConsentService
{
    // The access policy skips this and calls the repository, so it can pin the
    // same instant it stamps on the log row.
    Task<IReadOnlyList<DataCategory>> GetConsentedCategoriesAsync(
        int nextOfKinId,
        int careRecipientId,
        CancellationToken cancellationToken
    );
}
