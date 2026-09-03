namespace Parorendeportalen.Api.Services;

// The caller as this request sees them. Never the display name or subject claim.
public sealed record CurrentNextOfKin(int NextOfKinId, IReadOnlyList<int> CareRecipientIds);

public interface ICurrentNextOfKinAccessor
{
    // Null when the session's subject resolves to no next-of-kin row.
    Task<CurrentNextOfKin?> GetCurrentAsync(CancellationToken cancellationToken);

    // Empty when the caller holds no currently-valid grant.
    Task<IReadOnlyList<int>> GetCareRecipientIdsAsync(CancellationToken cancellationToken);

    Task<bool> HasAccessToAsync(int careRecipientId, CancellationToken cancellationToken);
}
