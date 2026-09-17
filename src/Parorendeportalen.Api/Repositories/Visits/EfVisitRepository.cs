using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models.Visits;

namespace Parorendeportalen.Api.Repositories.Visits;

public sealed class EfVisitRepository(AppDbContext context) : IVisitRepository
{
    public async Task<(IReadOnlyList<Visit> Items, int TotalCount)> GetByCareRecipientIdAsync(
        int careRecipientId,
        int viewerNextOfKinId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var query = VisibleTo(
                context
                    .Visits.AsNoTracking()
                    .Include(v => v.CareRecipient)
                    .Include(v => v.CreatedBy),
                viewerNextOfKinId
            )
            .Where(v => v.CareRecipientId == careRecipientId);

        if (from is not null)
        {
            query = query.Where(v => v.ScheduledAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(v => v.ScheduledAt <= to);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(v => v.ScheduledAt)
            .ThenBy(v => v.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Visit?> GetByIdAsync(
        int id,
        int careRecipientId,
        int viewerNextOfKinId,
        CancellationToken cancellationToken
    )
    {
        return await VisibleTo(
                context
                    .Visits.AsNoTracking()
                    .Include(v => v.CareRecipient)
                    .Include(v => v.CreatedBy),
                viewerNextOfKinId
            )
            .FirstOrDefaultAsync(
                v => v.Id == id && v.CareRecipientId == careRecipientId,
                cancellationToken
            );
    }

    public async Task<IReadOnlyList<Visit>> GetInRangeAsync(
        int careRecipientId,
        int viewerNextOfKinId,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken
    )
    {
        // Npgsql refuses a DateTimeOffset carrying any offset but zero against
        // timestamptz, parameters included, and the day plan's bounds arrive
        // here on +01:00 or +02:00. Same instant, offset dropped.
        var from = fromInclusive.ToUniversalTime();
        var to = toExclusive.ToUniversalTime();

        // Exclusive upper bound: a visit at the stroke of midnight belongs to
        // the day starting there, and an inclusive one would list it on both.
        return await VisibleTo(context.Visits.AsNoTracking(), viewerNextOfKinId)
            .Where(v =>
                v.CareRecipientId == careRecipientId && v.ScheduledAt >= from && v.ScheduledAt < to
            )
            .OrderBy(v => v.ScheduledAt)
            .ThenBy(v => v.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Visit> AddAsync(Visit visit, CancellationToken cancellationToken)
    {
        context.Visits.Add(visit);
        await context.SaveChangesAsync(cancellationToken);
        return visit;
    }

    public async Task<Visit?> GetForWriteAsync(
        int id,
        int careRecipientId,
        CancellationToken cancellationToken
    ) =>
        await context.Visits.FirstOrDefaultAsync(
            v => v.Id == id && v.CareRecipientId == careRecipientId,
            cancellationToken
        );

    public Task<bool> UpdateAsync(
        Visit visit,
        uint expectedVersion,
        CancellationToken cancellationToken
    ) => SaveAgainstVersionAsync(visit, expectedVersion, cancellationToken);

    public Task<bool> DeleteAsync(
        Visit visit,
        uint expectedVersion,
        CancellationToken cancellationToken
    )
    {
        context.Visits.Remove(visit);
        return SaveAgainstVersionAsync(visit, expectedVersion, cancellationToken);
    }

    // OriginalValue puts the expected version in the UPDATE's WHERE clause, so Postgres decides the race.
    private async Task<bool> SaveAgainstVersionAsync(
        Visit visit,
        uint expectedVersion,
        CancellationToken cancellationToken
    )
    {
        context.Entry(visit).Property(v => v.Version).OriginalValue = expectedVersion;

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

    // No author: record-owned, visible via consent alone. Authored: visible to others only if Shared.
    private static IQueryable<Visit> VisibleTo(IQueryable<Visit> visits, int viewerNextOfKinId) =>
        visits.Where(v =>
            v.CreatedByNextOfKinId == null
            || v.Visibility == Visibility.Shared
            || v.CreatedByNextOfKinId == viewerNextOfKinId
        );
}
