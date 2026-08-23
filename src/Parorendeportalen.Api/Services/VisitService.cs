using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Repositories;

namespace Parorendeportalen.Api.Services;

public sealed class VisitService(IVisitRepository repository) : IVisitService
{
    public async Task<PagedResponse<VisitResponse>> GetByCareRecipientIdAsync(
        int careRecipientId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var (visits, totalCount) = await repository.GetByCareRecipientIdAsync(
            careRecipientId, from, to, pageNumber, pageSize, cancellationToken);

        return new PagedResponse<VisitResponse>(
            visits.Select(v => v.ToResponse()).ToList(),
            pageNumber,
            pageSize,
            totalCount);
    }
}
