namespace Parorendeportalen.Api.Services;

public sealed class CurrentNextOfKinAccessor(
    IHttpContextAccessor httpContextAccessor,
    INextOfKinService nextOfKinService
) : ICurrentNextOfKinAccessor
{
    private CurrentNextOfKin? resolved;
    private bool hasResolved;

    // Cached per request (registered scoped): policy and service both ask, second ask shouldn't re-query.
    public async Task<CurrentNextOfKin?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        if (hasResolved)
        {
            return resolved;
        }

        var nextOfKin = await nextOfKinService.GetByExternalIdAsync(
            ExternalId(),
            cancellationToken
        );

        resolved = nextOfKin is null
            ? null
            : new CurrentNextOfKin(
                nextOfKin.Id,
                nextOfKin.Grants.Select(g => g.CareRecipientId).ToList()
            );
        hasResolved = true;

        return resolved;
    }

    public async Task<IReadOnlyList<int>> GetCareRecipientIdsAsync(
        CancellationToken cancellationToken
    ) =>
        await nextOfKinService.GetCareRecipientIdsByExternalIdAsync(
            ExternalId(),
            cancellationToken
        );

    public async Task<bool> HasAccessToAsync(
        int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        var careRecipientIds = await GetCareRecipientIdsAsync(cancellationToken);
        return careRecipientIds.Contains(careRecipientId);
    }

    private string ExternalId()
    {
        var user =
            httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException(
                "No HttpContext — this must be called from within a request."
            );

        return user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Authenticated request has no 'sub' claim.");
    }
}
