using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Repositories.Access;

namespace Parorendeportalen.Api.Services.Access;

public sealed class ConsentService(IConsentRepository repository, TimeProvider timeProvider)
    : IConsentService
{
    public Task<IReadOnlyList<DataCategory>> GetConsentedCategoriesAsync(
        int nextOfKinId,
        int careRecipientId,
        CancellationToken cancellationToken
    ) =>
        repository.GetActiveCategoriesAsync(
            nextOfKinId,
            careRecipientId,
            timeProvider.GetUtcNow(),
            cancellationToken
        );
}
