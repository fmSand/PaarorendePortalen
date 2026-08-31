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
        CancellationToken cancellationToken)
    {
        var query = context.Visits
            .AsNoTracking()
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

    public async Task<Visit?> GetByIdAsync(int id, int careRecipientId, CancellationToken cancellationToken)
    {
        return await context.Visits
            .AsNoTracking()
            .Include(v => v.CareRecipient)
            .FirstOrDefaultAsync(
                v => v.Id == id && v.CareRecipientId == careRecipientId,
                cancellationToken);
    }
}
