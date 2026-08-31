namespace Parorendeportalen.Api.Services;

public interface ICurrentNextOfKinAccessor
{
    // Empty when the caller holds no currently-valid grant.
    Task<IReadOnlyList<int>> GetCareRecipientIdsAsync(CancellationToken cancellationToken);

    Task<bool> HasAccessToAsync(int careRecipientId, CancellationToken cancellationToken);
}
