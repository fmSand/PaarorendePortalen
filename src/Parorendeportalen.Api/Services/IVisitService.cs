using Parorendeportalen.Api.Dtos;

namespace Parorendeportalen.Api.Services;

public interface IVisitService
{
    Task<PagedResponse<VisitResponse>> GetByCareRecipientIdAsync(
        int careRecipientId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);
}
