using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public interface IVisitRepository
{
    //pageNumber is 1-based; TotalCount is pre-paging, for computing page count
    Task<(IReadOnlyList<Visit> Items, int TotalCount)> GetByCareRecipientIdAsync(
        int careRecipientId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);
}
