using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public interface IVisitRepository
{
    //pageNumber is 1-based; TotalCount is pre-paging, for computing page count
    Task<(IReadOnlyList<Visit> Items, int TotalCount)> GetByCareRecipientIdAsync(
        int careRecipientId,
        int viewerNextOfKinId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken
    );

    Task<Visit?> GetByIdAsync(
        int id,
        int careRecipientId,
        int viewerNextOfKinId,
        CancellationToken cancellationToken
    );

    // Half-open [from, to). Unpaged, because the caller is the day plan and one
    // Norwegian day of one person's visits is a handful of rows.
    Task<IReadOnlyList<Visit>> GetInRangeAsync(
        int careRecipientId,
        int viewerNextOfKinId,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken
    );

    Task<Visit> AddAsync(Visit visit, CancellationToken cancellationToken);

    // Tracked, unfiltered by visibility: the service does the author check and must answer 403.
    Task<Visit?> GetForWriteAsync(int id, int careRecipientId, CancellationToken cancellationToken);

    // False when the row moved on since expectedVersion was read.
    Task<bool> UpdateAsync(Visit visit, uint expectedVersion, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Visit visit, uint expectedVersion, CancellationToken cancellationToken);
}
