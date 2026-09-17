using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models.Visits;

namespace Parorendeportalen.Api.Repositories.Visits;

public sealed class EfVisitCommentRepository(AppDbContext context) : IVisitCommentRepository
{
    public async Task<IReadOnlyList<VisitComment>> GetByVisitIdAsync(
        int visitId,
        int viewerNextOfKinId,
        CancellationToken cancellationToken
    ) =>
        await context
            .VisitComments.AsNoTracking()
            .Include(c => c.Author)
            .Where(c =>
                c.VisitId == visitId
                && (c.Visibility == Visibility.Shared || c.AuthorNextOfKinId == viewerNextOfKinId)
            )
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

    public async Task<VisitComment?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await context
            .VisitComments.AsNoTracking()
            .Include(c => c.Author)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<VisitComment> AddAsync(
        VisitComment comment,
        CancellationToken cancellationToken
    )
    {
        context.VisitComments.Add(comment);
        await context.SaveChangesAsync(cancellationToken);
        return comment;
    }

    public async Task<VisitComment?> GetForWriteAsync(
        int id,
        int visitId,
        CancellationToken cancellationToken
    ) =>
        await context.VisitComments.FirstOrDefaultAsync(
            c => c.Id == id && c.VisitId == visitId,
            cancellationToken
        );

    public Task<bool> UpdateAsync(
        VisitComment comment,
        uint expectedVersion,
        CancellationToken cancellationToken
    ) => SaveAgainstVersionAsync(comment, expectedVersion, cancellationToken);

    public Task<bool> DeleteAsync(
        VisitComment comment,
        uint expectedVersion,
        CancellationToken cancellationToken
    )
    {
        context.VisitComments.Remove(comment);
        return SaveAgainstVersionAsync(comment, expectedVersion, cancellationToken);
    }

    // OriginalValue puts the expected version in the UPDATE's WHERE clause, so Postgres decides the race.
    private async Task<bool> SaveAgainstVersionAsync(
        VisitComment comment,
        uint expectedVersion,
        CancellationToken cancellationToken
    )
    {
        context.Entry(comment).Property(c => c.Version).OriginalValue = expectedVersion;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
