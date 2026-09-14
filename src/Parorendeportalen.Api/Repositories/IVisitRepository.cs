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
        CancellationToken cancellationToken
    );

    Task<Visit?> GetByIdAsync(int id, int careRecipientId, CancellationToken cancellationToken);

    // Half-open [from, to). Unpaged, because the caller is the day plan and one
    // Norwegian day of one person's visits is a handful of rows.
    Task<IReadOnlyList<Visit>> GetInRangeAsync(
        int careRecipientId,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken
    );
}
