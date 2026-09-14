using Microsoft.EntityFrameworkCore;
using Parorendeportalen.Api.Data;
using Parorendeportalen.Api.Models;

namespace Parorendeportalen.Api.Repositories;

public sealed class EfVisitRepository(AppDbContext context) : IVisitRepository
{
    public async Task<(IReadOnlyList<Visit> Items, int TotalCount)> GetByCareRecipientIdAsync(
        int careRecipientId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var query = context
            .Visits.AsNoTracking()
            .Include(v => v.CareRecipient)
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
        CancellationToken cancellationToken
    )
    {
        return await context
            .Visits.AsNoTracking()
            .Include(v => v.CareRecipient)
            .FirstOrDefaultAsync(
                v => v.Id == id && v.CareRecipientId == careRecipientId,
                cancellationToken
            );
    }

    public async Task<IReadOnlyList<Visit>> GetInRangeAsync(
        int careRecipientId,
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
        return await context
            .Visits.AsNoTracking()
            .Where(v =>
                v.CareRecipientId == careRecipientId && v.ScheduledAt >= from && v.ScheduledAt < to
            )
            .OrderBy(v => v.ScheduledAt)
            .ThenBy(v => v.Id)
            .ToListAsync(cancellationToken);
    }
}
