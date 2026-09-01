namespace Parorendeportalen.Api.Services;

public sealed class CurrentNextOfKinAccessor(
    IHttpContextAccessor httpContextAccessor,
    INextOfKinService nextOfKinService
) : ICurrentNextOfKinAccessor
{
    public async Task<IReadOnlyList<int>> GetCareRecipientIdsAsync(
        CancellationToken cancellationToken
    )
    {
        var user =
            httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException(
                "No HttpContext — this must be called from within a request."
            );

        var externalId =
            user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Authenticated request has no 'sub' claim.");

        return await nextOfKinService.GetCareRecipientIdsByExternalIdAsync(
            externalId,
            cancellationToken
        );
    }

    public async Task<bool> HasAccessToAsync(
        int careRecipientId,
        CancellationToken cancellationToken
    )
    {
        var careRecipientIds = await GetCareRecipientIdsAsync(cancellationToken);
        return careRecipientIds.Contains(careRecipientId);
    }
}
