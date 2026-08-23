namespace Parorendeportalen.Api.Services;

public sealed class CurrentNextOfKinAccessor(
    IHttpContextAccessor httpContextAccessor,
    INextOfKinService nextOfKinService) : ICurrentNextOfKinAccessor
{
    public async Task<int> GetCareRecipientIdAsync(CancellationToken cancellationToken)
    {
        var user = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HttpContext — this must be called from within a request.");

        var externalId = user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Authenticated request has no 'sub' claim.");

        var careRecipientId = await nextOfKinService.GetCareRecipientIdByExternalIdAsync(externalId, cancellationToken);

        return careRecipientId
            ?? throw new InvalidOperationException($"No NextOfKin found for authenticated external id '{externalId}'.");
    }
}
