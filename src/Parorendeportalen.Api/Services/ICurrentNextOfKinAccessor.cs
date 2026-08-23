namespace Parorendeportalen.Api.Services;

public interface ICurrentNextOfKinAccessor
{
    Task<int> GetCareRecipientIdAsync(CancellationToken cancellationToken);
}
