using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public interface IVisitCommentRepository
{
    Task<IReadOnlyList<VisitComment>> GetByVisitIdAsync(
        int visitId,
        int viewerNextOfKinId,
        CancellationToken cancellationToken
    );

    // Includes the author for the response's display name. Unscoped: caller already knows visit and viewer.
    Task<VisitComment?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<VisitComment> AddAsync(VisitComment comment, CancellationToken cancellationToken);

    // Tracked, unfiltered by visibility: same reason as the visit write path, service answers 403.
    Task<VisitComment?> GetForWriteAsync(int id, int visitId, CancellationToken cancellationToken);

    // False when the row moved on since expectedVersion was read.
    Task<bool> UpdateAsync(
        VisitComment comment,
        uint expectedVersion,
        CancellationToken cancellationToken
    );

    Task<bool> DeleteAsync(
        VisitComment comment,
        uint expectedVersion,
        CancellationToken cancellationToken
    );
}
