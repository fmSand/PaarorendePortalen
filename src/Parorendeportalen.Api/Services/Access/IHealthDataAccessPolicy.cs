using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Access;

namespace Parorendeportalen.Api.Services.Access;

public sealed record ConsentedAccess(int NextOfKinId, IReadOnlyList<ConsentScope> Scopes);

public interface IHealthDataAccessPolicy
{
    // Writes the access-log row too, so an authorised read cannot go unlogged.
    Task<AccessDecision> AuthorizeReadAsync(
        int careRecipientId,
        DataCategory category,
        CancellationToken cancellationToken
    );

    // Same two gates, logged as a write: Normen requires logging who changed data.
    Task<AccessDecision> AuthorizeWriteAsync(
        int careRecipientId,
        DataCategory category,
        CancellationToken cancellationToken
    );

    // One Granted row per pair.
    // Null when the session resolves to nobody.
    Task<ConsentedAccess?> AuthorizeConsentedReadsAsync(CancellationToken cancellationToken);

    // The same scope without the log rows, for a write that reads no health
    // data but must still be held to the gate.
    Task<ConsentedAccess?> ResolveConsentedScopeAsync(CancellationToken cancellationToken);
}
