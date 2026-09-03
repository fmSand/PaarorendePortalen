namespace Parorendeportalen.Api.Services;

public sealed class CurrentNextOfKinAccessor(
    IHttpContextAccessor httpContextAccessor,
    INextOfKinService nextOfKinService
) : ICurrentNextOfKinAccessor
{
    public async Task<CurrentNextOfKin?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var nextOfKin = await nextOfKinService.GetByExternalIdAsync(
            ExternalId(),
            cancellationToken
        );

        return nextOfKin is null
            ? null
            : new CurrentNextOfKin(
                nextOfKin.Id,
                nextOfKin.Grants.Select(g => g.CareRecipientId).ToList()
            );
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
