using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Services;

public interface IHealthDataAccessPolicy
{
    // Writes the access-log row too, so an authorised read cannot go unlogged.
    Task<AccessDecision> AuthorizeReadAsync(
        int careRecipientId,
        DataCategory category,
        CancellationToken cancellationToken
    );
}
